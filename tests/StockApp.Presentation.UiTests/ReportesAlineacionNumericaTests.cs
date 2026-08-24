using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián del borrado de los 5 <c>DataGridCell.num</c> locales de Reportes (Task 10.1 del plan
/// de Fase B, 2026-08-19). Antes de borrar los 5 estilos redundantes (uno por vista), este test
/// verifica POSITIVAMENTE que la alineación a la derecha de las columnas numéricas sigue
/// funcionando -- y las dos mutaciones cruzadas (Steps 3 y 4, no versionadas, ver el ledger)
/// confirman que la alineación viene del estilo GLOBAL de <c>Themes/DataGrid.axaml:97</c> (cargado
/// en <c>TestAppBuilder.cs</c> desde la tanda 0), no del local.
///
/// Sin datos no hay <c>DataGridCell</c> realizadas (mismo fenómeno del Ruling B-8/B-16): se
/// asigna <c>ItemsSource</c> directo al <c>DataGrid</c> ya montado, sin pasar por un
/// <c>StockCategoriaViewModel</c> real -- no hace falta, el invariante es puramente de layout, no
/// de datos ni de comandos.
///
/// Se eligió <see cref="StockCategoriaView"/> (77 líneas, la más chica de las 5) como
/// representante: las 5 comparten EXACTAMENTE el mismo
/// <c>&lt;Style Selector="DataGridCell.num"&gt;</c> copiado, y el estilo global las cubre a todas
/// por igual -- no hay nada específico de <c>StockCategoriaView</c> en la alineación que este
/// test mide.
/// </summary>
public class ReportesAlineacionNumericaTests
{
    [AvaloniaFact]
    public void Montar_StockCategoriaViewConDatos_LasCeldasNumericasQuedanAlineadasADerecha()
    {
        var vista = new StockCategoriaView();
        var window = new Window { Width = 1000, Height = 600, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var grilla = vista.GetVisualDescendants().OfType<DataGrid>().Single();
        grilla.ItemsSource = new[]
        {
            new StockCategoriaDto("Ferretería", 12, 340m, 15000m),
            new StockCategoriaDto("Electricidad", 5, -3m, 8000m),
        };
        Dispatcher.UIThread.RunJobs();

        var celdasNum = window.GetVisualDescendants()
            .OfType<DataGridCell>()
            .Where(c => c.Classes.Contains("num"))
            .ToList();

        Assert.True(celdasNum.Count > 0,
            "No se realizó ninguna DataGridCell.num: sin celdas no hay nada que custodiar.");
        Assert.All(celdasNum, c =>
            Assert.Equal(HorizontalAlignment.Right, c.HorizontalContentAlignment));
    }
}
