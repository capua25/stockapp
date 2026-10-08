using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Columnas Auto de contenido libre sobre una DataGrid REAL con el tema v2: el ancho sale
/// del texto formateado de TODAS las filas (también las que la virtualización no realizó), con un tope,
/// elipsis y la reserva de la flecha de orden. Se mide el efecto (ActualWidth, texto pintado), no la configuración.
/// </summary>
public class ColumnaAutoTests
{
    private static ProductoDto P(int id, string codigo, decimal costo = 100m, string unidad = "Unidad") => new(
        id, codigo, null, $"Producto {id}", null, 1, "Construcción", null, 1, unidad, costo, 1m, 0m, true,
        new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private sealed record Montaje(Window Ventana, DataGrid Grilla, DataGridTemplateColumn Codigo, DataGridTemplateColumn Costo,
        DataGridTextColumn Unidad, DataGridTemplateColumn SinMedir);

    private static DataGridTemplateColumn ColumnaDeTexto(string header, string ruta, IValueConverter? conversor = null) => new()
    {
        Header = header,
        Width = DataGridLength.Auto,
        SortMemberPath = ruta,
        CellTemplate = new FuncDataTemplate<ProductoDto>((_, _) => new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding(ruta) { Converter = conversor },
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }),
    };

    /// <summary>Grilla de 300 de alto (virtualiza: ~6 filas realizadas) dentro de un scope .ui2, con una columna * como Productos.</summary>
    private static Montaje Montar(IEnumerable<ProductoDto> items, double ancho = 1000, Action<DataGrid>? configurar = null)
    {
        var codigo = ColumnaDeTexto("CÓDIGO", "Codigo");
        ColumnaAuto.SetMedir(codigo, true);
        var costo = ColumnaDeTexto("COSTO", "PrecioCosto", MonedaConverter.Instance);
        costo.CellStyleClasses.Add("num");
        ColumnaAuto.SetMedir(costo, true);
        ColumnaAuto.SetConversor(costo, MonedaConverter.Instance);
        var unidad = new DataGridTextColumn { Header = "UNIDAD", Width = DataGridLength.Auto, Binding = new Binding("UnidadMedidaNombre") };
        ColumnaAuto.SetMedir(unidad, true);
        var sinMedir = ColumnaDeTexto("CATEGORÍA", "CategoriaNombre");
        var nombre = ColumnaDeTexto("NOMBRE", "Nombre");
        nombre.Width = new DataGridLength(1, DataGridLengthUnitType.Star);

        var grilla = new DataGrid { Height = 300, IsReadOnly = true, CanUserSortColumns = true, CanUserResizeColumns = true };
        foreach (var c in new DataGridColumn[] { codigo, nombre, costo, unidad, sinMedir })
            grilla.Columns.Add(c);
        grilla.ItemsSource = new DataGridCollectionView(new ObservableCollection<ProductoDto>(items));
        configurar?.Invoke(grilla);

        var ventana = new Window { Width = ancho, Height = 400, Content = new UserControl { Classes = { "ui2" }, Content = grilla } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, grilla, codigo, costo, unidad, sinMedir);
    }

    private static ObservableCollection<ProductoDto> Fuente(Montaje m) =>
        (ObservableCollection<ProductoDto>)((DataGridCollectionView)m.Grilla.ItemsSource!).SourceCollection;

    private static void Recargar(Montaje m, IEnumerable<ProductoDto> items)
    {
        var fuente = Fuente(m);
        fuente.Clear();
        foreach (var p in items) fuente.Add(p);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static double AnchoNatural(TextBlock tb) => GrillasSinRecorteBarridoTests.AnchoNatural(tb);

    private static TextBlock TextoDeCelda(DataGrid grilla, object item, string texto) =>
        grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, item))
            .GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == texto);

    private static DataGridColumnHeader Header(DataGrid grilla, string titulo) =>
        grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, titulo));

    /// <summary>El TextBlock pintado en la celda de la fila de <paramref name="item"/> que está bajo el header <paramref name="titulo"/>.</summary>
    private static TextBlock CeldaBajoHeader(DataGrid grilla, object item, string titulo)
    {
        var header = Header(grilla, titulo);
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, 0), grilla)!.Value.X;
        var celda = grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, item))
            .GetVisualDescendants().OfType<DataGridCell>().Single(c =>
            {
                var x0 = c.TranslatePoint(default, grilla)!.Value.X;
                return x0 <= centro && centro <= x0 + c.Bounds.Width;
            });
        return celda.GetVisualDescendants().OfType<TextBlock>().First();
    }

    private static void ClickEnHeader(Montaje m, string titulo)
    {
        var header = Header(m.Grilla, titulo);
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseDown(centro, MouseButton.Left);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static double[] Anchos(DataGrid grilla) => grilla.Columns.Select(c => c.ActualWidth).ToArray();

    private static List<ProductoDto> MilCortosYUnoLargoAlFinal(string codigoLargo)
    {
        var items = Enumerable.Range(1, 999).Select(i => P(i, $"C-{i:0000}")).ToList();
        items.Add(P(1000, codigoLargo, 98765432.10m, "Metro cúbico"));
        return items;
    }

    [AvaloniaFact]
    public void ConMilFilas_ElAnchoEsElDelTextoMasLargoAunqueNoEsteRealizado_YScrollearNoLoMueve()
    {
        // "2x1,5" adentro: la sonda tiene que medir sin calt, como la celda.
        var items = MilCortosYUnoLargoAlFinal("MAT-2x1,5-CONSTRUCCION");
        var m = Montar(items);
        Assert.DoesNotContain(m.Grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, items[^1]));
        var antes = Anchos(m.Grilla);

        m.Grilla.ScrollIntoView(items[^1], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var celda = TextoDeCelda(m.Grilla, items[^1], "MAT-2x1,5-CONSTRUCCION");
        Assert.Equal(Math.Ceiling(AnchoNatural(celda) + 16), m.Codigo.ActualWidth, 1);
        var costo = TextoDeCelda(m.Grilla, items[^1], "$ 98.765.432,10");
        Assert.Equal(Math.Ceiling(AnchoNatural(costo) + 16), m.Costo.ActualWidth, 1);
        Assert.Equal(antes, Anchos(m.Grilla));
    }

    [AvaloniaFact]
    public void ColumnaDeTexto_SeMideConSuBinding()
    {
        var items = Enumerable.Range(1, 200).Select(i => P(i, $"C-{i}", unidad: i == 200 ? "Kilogramo por metro lineal" : "Par")).ToList();
        var m = Montar(items);

        Assert.Equal("Kilogramo por metro lineal", ColumnaAuto.Texto(m.Unidad, items[^1]));
        m.Grilla.ScrollIntoView(items[^1], null);
        Dispatcher.UIThread.RunJobs();
        var celda = TextoDeCelda(m.Grilla, items[^1], "Kilogramo por metro lineal");
        Assert.Equal(Math.Ceiling(AnchoNatural(celda) + 16), m.Unidad.ActualWidth, 1);
    }

    [AvaloniaFact]
    public void Paridad_ElTextoMedidoEsElTextoPintado()
    {
        var items = Enumerable.Range(1, 5).Select(i => P(i, $"MAT-{i:0000}", 1234.5m * i)).ToList();
        var m = Montar(items);

        foreach (var p in items)
        {
            Assert.Equal(CeldaBajoHeader(m.Grilla, p, "CÓDIGO").Text, ColumnaAuto.Texto(m.Codigo, p));
            Assert.Equal(CeldaBajoHeader(m.Grilla, p, "COSTO").Text, ColumnaAuto.Texto(m.Costo, p));
            Assert.Equal(CeldaBajoHeader(m.Grilla, p, "UNIDAD").Text, ColumnaAuto.Texto(m.Unidad, p));
        }
    }

    [AvaloniaFact]
    public void TextoMasLargoQueElTope_LaColumnaQuedaEnElTopeYLaCeldaSeCortaConElipsis()
    {
        var largo = "CODIGO-INTERNO-DE-PROVEEDOR-EXTRANJERO-0000000001";
        var m = Montar([P(1, largo), P(2, "C-2")]);

        Assert.Equal(ColumnaAuto.TopePorDefecto, m.Codigo.ActualWidth, 1);
        var celda = TextoDeCelda(m.Grilla, Fuente(m)[0], largo);
        Assert.Contains(celda.TextLayout.TextLines, l => l.HasCollapsed);
    }

    [AvaloniaFact]
    public void Ordenar_ElTituloYLaFlechaEntranEnElHeader_YNoSeMuevenAnchos()
    {
        // Contenido más angosto que el título: el ancho lo decide el header con la reserva de la flecha.
        var m = Montar(Enumerable.Range(1, 30).Select(i => P(i, $"C{i}")));
        var antes = Anchos(m.Grilla);

        var header = Header(m.Grilla, "CÓDIGO");
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseDown(centro, MouseButton.Left);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var flecha = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "SortIcon");
        Assert.True(flecha.IsEffectivelyVisible, "precondición: la columna quedó ordenada");
        var bordeFlecha = flecha.TranslatePoint(new Point(flecha.Bounds.Width, 0), header)!.Value.X;
        Assert.True(bordeFlecha <= header.Bounds.Width + 0.5, $"La flecha termina en {bordeFlecha:F1} y el header mide {header.Bounds.Width:F1}.");
        var titulo = header.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "CÓDIGO");
        Assert.True(titulo.Bounds.Width >= AnchoNatural(titulo) - 0.5, "El título ordenado quedó cortado.");
        Assert.Equal(antes, Anchos(m.Grilla));
    }

    [AvaloniaFact]
    public void Recarga_ConDatosMasAngostosNoAchica_YConMasAnchosAgranda()
    {
        var m = Montar([P(1, "MAT-CONSTRUCCION-0001")]);
        var ancho = m.Codigo.ActualWidth;

        Recargar(m, [P(2, "C-2")]);
        Assert.Equal(ancho, m.Codigo.ActualWidth, 1);

        Recargar(m, [P(3, "MAT-CONSTRUCCION-REFORZADA-0003")]);
        Assert.True(m.Codigo.ActualWidth > ancho + 10, $"No creció: {ancho:F1} -> {m.Codigo.ActualWidth:F1}");
    }

    [AvaloniaFact]
    public void UsuarioArrastraLaColumna_UnaRecargaConDatosMasAnchosNoLaMueve()
    {
        var m = Montar(Enumerable.Range(1, 10).Select(i => P(i, $"MAT-{i:0000}")));
        var header = Header(m.Grilla, "CÓDIGO");
        var borde = header.TranslatePoint(new Point(header.Bounds.Width - 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseMove(borde);
        m.Ventana.MouseDown(borde, MouseButton.Left);
        for (var paso = 1; paso <= 5; paso++)
        {
            m.Ventana.MouseMove(borde + new Point(40 * paso / 5.0, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }
        m.Ventana.MouseUp(borde + new Point(40, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var arrastrado = m.Codigo.ActualWidth;

        Recargar(m, [P(99, "MAT-CONSTRUCCION-REFORZADA-MUY-LARGA-0099")]);

        Assert.Equal(arrastrado, m.Codigo.ActualWidth, 1);
    }

    [AvaloniaFact]
    public void GrillaVacia_NoTocaLasColumnas()
    {
        var m = Montar([]);

        Assert.True(m.Codigo.Width.IsAuto);
        Assert.True(m.Costo.Width.IsAuto);
    }

    [AvaloniaFact]
    public void ColumnaSinMedir_SigueSiendoAuto()
    {
        var m = Montar(Enumerable.Range(1, 10).Select(i => P(i, $"C-{i}")));

        Assert.True(m.SinMedir.Width.IsAuto);
        Assert.True(m.Codigo.Width.IsAbsolute);
    }

    [AvaloniaFact]
    public void MinWidthDeLaColumna_GanaSobreElContenido()
    {
        var m = Montar([P(1, "C")]);
        m.Costo.MinWidth = 150;

        Recargar(m, [P(2, "C2", 1m)]);

        // El ActualWidth solo no alcanza: la DataGrid ya lo acota a su MinWidth aunque el ancho asignado sea menor.
        // Se mira el Width en píxeles que fijó la medición.
        Assert.True(m.Costo.Width.IsAbsolute, $"COSTO no se midió: {m.Costo.Width.UnitType}");
        Assert.True(m.Costo.Width.Value >= 150, $"La medición le asignó {m.Costo.Width.Value:F1} con MinWidth 150");
        Assert.True(m.Costo.ActualWidth >= 150 - 0.5, $"COSTO mide {m.Costo.ActualWidth:F1}");
    }

    [AvaloniaFact]
    public void CincoMilFilas_MedirNoAgregaCostoPerceptible()
    {
        static long Cargar(bool medir, List<ProductoDto> items)
        {
            var m = Montar([]);
            foreach (var c in m.Grilla.Columns) ColumnaAuto.SetMedir(c, medir && ColumnaAuto.GetMedir(c));
            var sw = Stopwatch.StartNew();
            Recargar(m, items);
            sw.Stop();
            m.Ventana.Close();
            return sw.ElapsedMilliseconds;
        }

        var items = Enumerable.Range(1, 5000).Select(i => P(i, $"MAT-{i:00000}", 1000m + i, i % 7 == 0 ? "Metro cúbico" : "Unidad")).ToList();
        Cargar(true, items); // calentamiento (JIT)
        var sin = Cargar(false, items);
        var con = Cargar(true, items);

        Assert.True(con <= sin * 1.5 + 400, $"5000 filas: con medición {con} ms vs sin {sin} ms");
    }

    [AvaloniaFact]
    public void OrdenarYFiltrar_NoVuelvenARecorrerLaFuente_YAgregarAEllaSi()
    {
        var m = Montar(Enumerable.Range(1, 50).Select(i => P(i, $"C-{i}")));
        var vista = (DataGridCollectionView)m.Grilla.ItemsSource!;
        var antes = DataGridColumnasAutoBehavior.RecorridosDeLaFuente(m.Grilla);
        Assert.True(antes > 0, "precondición: la carga inicial se midió");

        ClickEnHeader(m, "CÓDIGO");
        Assert.Single(vista.SortDescriptions);
        Assert.Equal(antes, DataGridColumnasAutoBehavior.RecorridosDeLaFuente(m.Grilla));

        vista.Filter = o => ((ProductoDto)o).Id % 2 == 0;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, vista.Count);
        Assert.Equal(antes, DataGridColumnasAutoBehavior.RecorridosDeLaFuente(m.Grilla));

        // Fila nueva que el filtro oculta: igual se mide, porque se mide la fuente entera.
        var ancho = m.Codigo.ActualWidth;
        Fuente(m).Add(P(51, "MAT-CONSTRUCCION-REFORZADA-0051"));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(DataGridColumnasAutoBehavior.RecorridosDeLaFuente(m.Grilla) > antes, "Agregar a la fuente no volvió a medir.");
        Assert.True(m.Codigo.ActualWidth > ancho + 10, $"No creció: {ancho:F1} -> {m.Codigo.ActualWidth:F1}");
    }

    [AvaloniaFact]
    public void ColumnaMarcadaConAnchoFijoOEstrella_ConservaSuAncho()
    {
        var m = Montar(Enumerable.Range(1, 10).Select(i => P(i, $"MAT-CONSTRUCCION-{i:0000}")), configurar: g =>
        {
            var categoria = g.Columns.Single(c => Equals(c.Header, "CATEGORÍA"));
            categoria.Width = new DataGridLength(50);
            ColumnaAuto.SetMedir(categoria, true);
            ColumnaAuto.SetMedir(g.Columns.Single(c => Equals(c.Header, "NOMBRE")), true);
        });
        var nombre = m.Grilla.Columns.Single(c => Equals(c.Header, "NOMBRE"));

        Assert.True(m.Codigo.Width.IsAbsolute, "precondición: las Auto marcadas se midieron");
        Assert.True(m.SinMedir.Width.IsAbsolute && m.SinMedir.Width.Value == 50,
            $"CATEGORÍA fija en 50 quedó en {m.SinMedir.Width.Value:F1} {m.SinMedir.Width.UnitType}");
        Assert.Equal(50, m.SinMedir.ActualWidth, 1);
        Assert.True(nombre.Width.IsStar, $"NOMBRE dejó de ser *: {nombre.Width}");
    }

    [AvaloniaFact]
    public void ColumnaMarcadaQueNoSePuedeMedir_NoQuedaPendienteNiFrenaALasSiguientes()
    {
        // Header vacío: no hay título del que copiar la tipografía, así que esa columna nunca se puede medir.
        var m = Montar(Enumerable.Range(1, 30).Select(i => P(i, $"MAT-{i:0000}")), configurar: g =>
        {
            var sinTitulo = ColumnaDeTexto("", "CategoriaNombre");
            ColumnaAuto.SetMedir(sinTitulo, true);
            g.Columns.Insert(0, sinTitulo);
        });

        Assert.True(m.Codigo.Width.IsAbsolute, "CÓDIGO, a la derecha de la que no se puede medir, quedó sin medir.");

        void Pasadas()
        {
            for (var pasada = 0; pasada < 2 * DataGridColumnasAutoBehavior.MaxIntentosDeMedicion; pasada++)
            {
                m.Grilla.InvalidateMeasure();
                Dispatcher.UIThread.RunJobs();
            }
        }
        Pasadas();
        var recorridos = DataGridColumnasAutoBehavior.RecorridosDeLaFuente(m.Grilla);
        Assert.InRange(recorridos, 1, DataGridColumnasAutoBehavior.MaxIntentosDeMedicion);
        Pasadas();
        Assert.Equal(recorridos, DataGridColumnasAutoBehavior.RecorridosDeLaFuente(m.Grilla));
    }
}
