using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Barrido del "hueco a la derecha" en las grillas de las Views REALES (mismas vistas y datos que
/// <see cref="GrillasSinRecorteBarridoTests"/>, sin envolver en <c>.ui2</c>: así las monta el shell,
/// cuya región de contenido no tiene esa clase). Un texto que no entra en su columna se corta en
/// el borde derecho de su TextBlock; si entre ese borde y el de la celda/header hay mucho lugar, la
/// columna muestra "Prueba U…" o "Product" con un hueco vacío al lado (bug 2026-10-05: 45 px en el
/// header — padding 12 + 32 reservados para la flecha de orden + separador — y 16 a 24 en celdas).
///
/// Se mide el EFECTO por geometría: borde derecho de la celda (o header) menos borde derecho del
/// TextBlock que ocupa el ancho disponible (alineación Stretch o Right). Tope 5 px: padding
/// derecho de 4 + el separador de 1 del header. La medición en píxeles de tinta, con Skia, está en
/// VisualTests/GrillaEspacioDerechoVistaRealTests (Historial de movimientos).
/// </summary>
public class GrillasEspacioDerechoBarridoTests
{
    private const double EspacioMaximoPx = 5;

    private static double X0(Visual v, Visual raiz) => v.TranslatePoint(new Point(0, 0), raiz)!.Value.X;

    /// <summary>TextBlocks que ocupan el lugar disponible (no los alineados a la izquierda o al centro, ni los de un control propio como un botón o una insignia).</summary>
    private static IEnumerable<TextBlock> TextosQueLlenanSuLugar(Control contenedor) =>
        contenedor.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)
                        && t.HorizontalAlignment is HorizontalAlignment.Stretch or HorizontalAlignment.Right
                        && !t.GetVisualAncestors().TakeWhile(a => a != contenedor).OfType<TemplatedControl>().Any());

    /// <summary>DataGridCell.OwningColumn es interno.</summary>
    private static DataGridColumn? ColumnaDe(DataGridCell celda) =>
        celda.GetType().GetProperty("OwningColumn",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(celda) as DataGridColumn;

    [AvaloniaFact]
    public void TextoDeCeldasYHeaders_TerminaANoMasDe5PxDelBordeDerecho_EnTodasLasVistas()
    {
        var fallas = new List<string>();
        foreach (var e in GrillasSinRecorteBarridoTests.Escenarios)
        {
            var (window, grid, _) = GrillasSinRecorteBarridoTests.Montar(e.Vista(), 1366 - GrillasSinRecorteBarridoTests.SidebarPx);
            GrillasSinRecorteBarridoTests.Cargar(grid, e.Datos(grid));
            var medidos = 0;

            foreach (var header in grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                         .Where(h => h.IsEffectivelyVisible && h.Bounds.Width > 0
                                     && GrillasSinRecorteBarridoTests.HeaderDeColumna(grid, h) is not null))
            {
                foreach (var tb in TextosQueLlenanSuLugar(header))
                {
                    medidos++;
                    var espacio = X0(header, window) + header.Bounds.Width - (X0(tb, window) + tb.Bounds.Width);
                    if (espacio > EspacioMaximoPx)
                        fallas.Add($"[{e.Nombre}] header '{tb.Text}': {espacio:F0}px (Padding={header.Padding})");
                }
            }

            foreach (var celda in grid.GetVisualDescendants().OfType<DataGridCell>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0))
            {
                foreach (var tb in TextosQueLlenanSuLugar(celda))
                {
                    medidos++;
                    var espacio = X0(celda, window) + celda.Bounds.Width - (X0(tb, window) + tb.Bounds.Width);
                    if (espacio > EspacioMaximoPx)
                        fallas.Add($"[{e.Nombre}] celda de '{ColumnaDe(celda)?.Header}': {espacio:F0}px (Padding={celda.Padding}, Margin del texto={tb.Margin})");
                }
            }

            Assert.True(medidos > 0, $"[{e.Nombre}] no se midió ningún texto: el barrido no custodia nada.");
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(fallas.Count == 0, "Hueco a la derecha del texto:\n" + string.Join("\n", fallas.Distinct()));
    }
}
