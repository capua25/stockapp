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

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Guardián del hueco a la derecha del texto cortado, medido sobre un DataGrid SINTÉTICO del tema viejo: SIN la clase
/// <c>.ui2</c> (como la región de contenido del shell, ver ShellMainView.axaml), o sea con Themes/DataGrid.axaml directamente y
/// sin depender de ninguna vista de la app (las vistas migran a v2 y se borran: este guardián ya pasó de Historial de
/// movimientos a Historial por producto y a Zonas, y ahora no se mueve más). El bug lo reportó el usuario en Historial de
/// movimientos. Render Skia: px entre el último píxel de tinta y el borde derecho del header o la celda, con la columna
/// "Nombre" angostada a 50 px y "Prueba UI V2", que no entra.
/// Antes del fix (4ccb347): header "Product" con 45 px de hueco (padding 12 + 32 reservados para
/// la flecha de orden + separador) y celda "Prueba U…" con 17 (padding 12 + Margin 4 de la vista).
/// Tope del header 6: padding derecho 4 + separador 1 + 1 de antialiasing del glifo (el header
/// recorta, no pone "…"). Tope de la celda: ver CeldaComentario_TextoConEllipsisLlegaHastaElBorde.
/// El barrido por geometría de las demás vistas está en UiTests/GrillasEspacioDerechoBarridoTests.
/// </summary>
public class GrillaEspacioDerechoVistaRealTests
{
    private const int EspacioMaximoPx = 6;
    private const double AnchoComentario = 50;

    private sealed record Fila(string Nombre, string Estado);

    private static DataGridTemplateColumn Columna(string header, string ruta, DataGridLength ancho) => new()
    {
        Header = header,
        Width = ancho,
        SortMemberPath = ruta,
        CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Fila>((_, _) => new TextBlock
        {
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(ruta),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        }),
    };

    /// <summary>Las dos formas de columna de texto que hay en las grillas del tema viejo: de plantilla ("Nombre") y DataGridTextColumn ("Observaciones").</summary>
    public static readonly string[] Columnas = ["Nombre", "Observaciones"];

    private static (Window Ventana, DataGrid Grilla, DataGridColumn Comentario) Montar(string columna)
    {
        // Misma forma que las grillas del tema viejo: columnas de texto, una * de relleno, orden por click y resize.
        var nombre = Columna("Nombre", nameof(Fila.Nombre), new DataGridLength(AnchoComentario));
        nombre.MinWidth = AnchoComentario;
        var texto = new DataGridTextColumn
        {
            Header = "Observaciones", Binding = new Avalonia.Data.Binding(nameof(Fila.Nombre)), Width = new DataGridLength(AnchoComentario), MinWidth = AnchoComentario,
        };
        var grilla = new DataGrid { IsReadOnly = true, CanUserSortColumns = true, CanUserResizeColumns = true, Height = 300 };
        grilla.Columns.Add(nombre);
        grilla.Columns.Add(texto);
        grilla.Columns.Add(Columna("Estado", nameof(Fila.Estado), new DataGridLength(1, DataGridLengthUnitType.Star)));
        var filas = Enumerable.Range(0, 3).Select(_ => new Fila("Prueba UI V2", "Activa")).ToList();
        grilla.ItemsSource = new DataGridCollectionView(filas);
        var vista = new UserControl { Content = grilla };   // sin la clase ui2: tema viejo
        var ventana = new Window { Width = 400, Height = 400, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Refrescar(ventana);
        // Precondición: el header y la celda tienen que quedar CORTADOS; si no, no hay hueco que medir.
        var textoHeader = HeaderComentario(grilla, columna).GetVisualDescendants().OfType<TextBlock>().Single();
        var textoCelda = CeldaComentario(ventana, grilla, columna).GetVisualDescendants().OfType<TextBlock>().Single();
        foreach (var tb in new[] { textoHeader, textoCelda })
        {
            Assert.True(tb.Bounds.Width < AnchoNatural(tb) - 1,
                $"'{tb.Text}' entra entero ({tb.Bounds.Width:0.#} de {AnchoNatural(tb):0.#}px, columna de {HeaderComentario(grilla, columna).Bounds.Width:0}): no hay corte que medir.");
        }
        return (ventana, grilla, columna == "Nombre" ? nombre : texto);
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

    private static DataGridColumnHeader HeaderComentario(DataGrid grilla, string columna) =>
        grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => h.Content as string == columna);

    private static DataGridCell CeldaComentario(Window ventana, DataGrid grilla, string columna)
    {
        var x = HeaderComentario(grilla, columna).TranslatePoint(new Point(0, 0), ventana)!.Value.X;
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

    [AvaloniaTheory]
    [InlineData("Nombre")]
    [InlineData("Observaciones")]
    public void HeaderComentario_TextoCortadoLlegaHastaElBorde_SinReservaParaLaFlechaDeOrden(string columna)
    {
        var (ventana, grilla, _) = Montar(columna);
        var header = HeaderComentario(grilla, columna);
        var espacio = EspacioDerecho(ventana, header);
        Assert.True(espacio <= EspacioMaximoPx,
            $"Header '{columna}' de {header.Bounds.Width:0}px: {espacio}px vacíos a la derecha del texto cortado (Padding={header.Padding}).");
    }

    /// <summary>
    /// La celda corta con "…" por CARÁCTER: entre el "…" y el final del lugar del texto puede sobrar
    /// hasta un carácter que no entró (granularidad del corte, no un hueco del tema). Tope: padding
    /// derecho 4 + 1 de antialiasing + el carácter más ancho del texto. Antes del fix el piso era 16
    /// (padding 12 + Margin 4) MÁS esa misma holgura.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Nombre")]
    [InlineData("Observaciones")]
    public void CeldaComentario_TextoConEllipsisLlegaHastaElBorde(string columna)
    {
        var (ventana, grilla, _) = Montar(columna);
        var celda = CeldaComentario(ventana, grilla, columna);
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
        var (ventana, grilla, comentario) = Montar("Nombre");
        comentario.Sort(direccion);
        Refrescar(ventana);
        var header = HeaderComentario(grilla, "Nombre");
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
