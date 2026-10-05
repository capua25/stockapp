using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Views.Movimientos;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Guardián del hueco a la derecha del texto cortado, medido en la VISTA REAL que reportó el
/// usuario (Historial de movimientos), montada como la monta el shell: SIN la clase <c>.ui2</c>
/// (la región de contenido no la tiene, ver ShellMainView.axaml), o sea con Themes/DataGrid.axaml.
/// Render Skia: px entre el último píxel de tinta y el borde derecho del header o la celda, con
/// la columna Producto angostada a 60 px y "Prueba UI V2", que no entra.
/// Antes del fix (4ccb347): header "Product" con 45 px de hueco (padding 12 + 32 reservados para
/// la flecha de orden + separador) y celda "Prueba U…" con 17 (padding 12 + Margin 4 de la vista).
/// Tope del header 6: padding derecho 4 + separador 1 + 1 de antialiasing del glifo (el header
/// recorta, no pone "…"). Tope de la celda: ver CeldaProducto_TextoConEllipsisLlegaHastaElBorde.
/// El barrido por geometría de las demás vistas está en UiTests/GrillasEspacioDerechoBarridoTests.
/// </summary>
public class GrillaEspacioDerechoVistaRealTests
{
    private const int EspacioMaximoPx = 6;
    private const double AnchoProducto = 60;

    private static (Window Ventana, DataGrid Grilla, DataGridColumn Producto) Montar()
    {
        var vista = new MovimientoHistorialView();
        // Como si el usuario la hubiese angostado: piso propio de 60 (no el de 160 del behavior) y una
        // ventana de 1000 en la que las columnas * no entran, así Producto queda en su piso.
        var producto = vista.GetLogicalDescendants().OfType<DataGrid>().First().Columns.First(c => c.Header as string == "Producto");
        DataGridColumnasAutoBehavior.SetPisoProporcional(producto, AnchoProducto);
        var ventana = new Window { Width = 1000, Height = 600, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        var grilla = vista.GetLogicalDescendants().OfType<DataGrid>().First();
        var filas = Enumerable.Range(0, 3).Select(i => new MovimientoHistorialDto(i, i, "Prueba UI V2", TipoMovimiento.Entrada,
            MotivoMovimiento.Compra, 1, 1, 1, 1, "c", new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc), 1, "admin")).ToList();
        grilla.ItemsSource = new DataGridCollectionView(filas);
        Dispatcher.UIThread.RunJobs();
        producto.Width = new DataGridLength(AnchoProducto);
        producto.MinWidth = AnchoProducto;
        Refrescar(ventana);
        // Precondición: el header y la celda tienen que quedar CORTADOS; si no, no hay hueco que medir.
        var textoHeader = HeaderProducto(grilla).GetVisualDescendants().OfType<TextBlock>().Single();
        var textoCelda = CeldaProducto(ventana, grilla).GetVisualDescendants().OfType<TextBlock>().Single();
        foreach (var tb in new[] { textoHeader, textoCelda })
        {
            Assert.True(tb.Bounds.Width < AnchoNatural(tb) - 1,
                $"'{tb.Text}' entra entero ({tb.Bounds.Width:0.#} de {AnchoNatural(tb):0.#}px, columna de {HeaderProducto(grilla).Bounds.Width:0}): no hay corte que medir.");
        }
        return (ventana, grilla, producto);
    }

    private static double AnchoNatural(TextBlock tb)
    {
        var gemelo = new TextBlock { Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight, FontFamily = tb.FontFamily, LetterSpacing = tb.LetterSpacing };
        gemelo.Measure(Size.Infinity);
        return gemelo.DesiredSize.Width;
    }

    private static void Refrescar(Window ventana)
    {
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridColumnHeader HeaderProducto(DataGrid grilla) =>
        grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => h.Content as string == "Producto");

    private static DataGridCell CeldaProducto(Window ventana, DataGrid grilla)
    {
        var x = HeaderProducto(grilla).TranslatePoint(new Point(0, 0), ventana)!.Value.X;
        return grilla.GetVisualDescendants().OfType<DataGridCell>()
            .First(c => Math.Abs(c.TranslatePoint(new Point(0, 0), ventana)!.Value.X - x) < 1);
    }

    /// <summary>Px entre la última columna con tinta y el borde derecho del control (se ignora su último px: separador).</summary>
    private static int EspacioDerecho(Window ventana, Control control)
    {
        var frame = ventana.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sin frame.");
        var origen = control.TranslatePoint(new Point(0, 0), ventana)!.Value;
        int izq = (int)Math.Round(origen.X), arriba = (int)Math.Round(origen.Y);
        int ancho = (int)Math.Round(control.Bounds.Width), alto = (int)Math.Round(control.Bounds.Height);
        using var fb = frame.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        int Pix(int x, int y, int c) => bytes[y * fb.RowBytes + x * 4 + c];
        for (var x = izq + ancho - 2; x >= izq; x--)
            for (var y = arriba + 4; y < arriba + alto - 4; y++)
                if (Enumerable.Range(0, 3).Any(c => Math.Abs(Pix(x, y, c) - Pix(izq + 1, arriba + 2, c)) > 40))
                    return izq + ancho - 1 - x;
        throw new InvalidOperationException($"Sin tinta en {control.GetType().Name}: la medición no custodia nada.");
    }

    [AvaloniaFact]
    public void HeaderProducto_TextoCortadoLlegaHastaElBorde_SinReservaParaLaFlechaDeOrden()
    {
        var (ventana, grilla, _) = Montar();
        var header = HeaderProducto(grilla);
        var espacio = EspacioDerecho(ventana, header);
        Assert.True(espacio <= EspacioMaximoPx,
            $"Header 'Producto' de {header.Bounds.Width:0}px: {espacio}px vacíos a la derecha del texto cortado (Padding={header.Padding}).");
    }

    /// <summary>
    /// La celda corta con "…" por CARÁCTER: entre el "…" y el final del lugar del texto puede sobrar
    /// hasta un carácter que no entró (granularidad del corte, no un hueco del tema). Tope: padding
    /// derecho 4 + 1 de antialiasing + el carácter más ancho del texto. Antes del fix el piso era 16
    /// (padding 12 + Margin 4) MÁS esa misma holgura.
    /// </summary>
    [AvaloniaFact]
    public void CeldaProducto_TextoConEllipsisLlegaHastaElBorde()
    {
        var (ventana, grilla, _) = Montar();
        var celda = CeldaProducto(ventana, grilla);
        var texto = celda.GetVisualDescendants().OfType<TextBlock>().Single();
        var caracterMasAncho = texto.Text!.Select(c => AnchoNatural(new TextBlock
            { Text = c.ToString(), FontSize = texto.FontSize, FontWeight = texto.FontWeight, FontFamily = texto.FontFamily })).Max();
        var tope = 5 + (int)Math.Ceiling(caracterMasAncho);
        var espacio = EspacioDerecho(ventana, celda);
        Assert.True(espacio <= tope,
            $"Celda 'Prueba UI V2' de {celda.Bounds.Width:0}px: {espacio}px vacíos, tope {tope} (Padding={celda.Padding}, Margin del texto={texto.Margin}).");
    }

    /// <summary>Sin la reserva fija de 32 px, la flecha tiene que seguir apareciendo al ordenar, separada del texto y adentro del header.</summary>
    [AvaloniaTheory]
    [InlineData(ListSortDirection.Ascending)]
    [InlineData(ListSortDirection.Descending)]
    public void ColumnaOrdenada_MuestraLaFlechaSeparadaDelTextoYDentroDelHeader(ListSortDirection direccion)
    {
        var (ventana, grilla, producto) = Montar();
        producto.Sort(direccion);
        Refrescar(ventana);
        var header = HeaderProducto(grilla);
        var flecha = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "SortIcon");
        var presentador = header.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
        Assert.True(flecha.IsEffectivelyVisible && flecha.Bounds.Width > 0 && flecha.Bounds.Height > 0,
            $"La flecha de orden no se ve: visible={flecha.IsEffectivelyVisible} {flecha.Bounds}");
        var xTexto = presentador.TranslatePoint(new Point(presentador.Bounds.Width, 0), header)!.Value.X;
        var xFlecha = flecha.TranslatePoint(new Point(0, 0), header)!.Value.X;
        var finFlecha = xFlecha + flecha.Bounds.Width;
        Assert.True(xFlecha - xTexto >= 7.5, $"La flecha queda pegada al texto: texto hasta x={xTexto:0.#}, flecha desde x={xFlecha:0.#}.");
        Assert.True(finFlecha <= header.Bounds.Width - 1, $"La flecha se sale del header: termina en x={finFlecha:0.#} de {header.Bounds.Width:0}.");
    }
}
