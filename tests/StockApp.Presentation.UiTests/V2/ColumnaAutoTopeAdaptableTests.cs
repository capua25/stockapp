using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// El tope de una columna medida se adapta al espacio libre: una unidad más larga que lo que queda en la grilla
/// se corta con elipsis en vez de empujar a las demás columnas fuera del borde. Grilla real con el tema v2 y las
/// columnas de Productos (CÓDIGO, COSTO y UNIDAD medidas; NOMBRE 2* y CATEGORÍA * con piso 160; STOCK 220 y
/// ESTADO 78 fijas). Grilla de 950 y 1036: la que deja la vista a 1280x760 y a 1366x768 con el sidebar de 240.
/// </summary>
public class ColumnaAutoTopeAdaptableTests
{
    private const double Grilla1280 = 950;
    private const double Grilla1366 = 1036;

    /// <summary>Unidad más larga que el tope de 240 con la tipografía de la celda (la misma de los mockups).</summary>
    private const string UnidadLarga = "Metro cúbico puesto en obra con flete incluido";

    private static readonly DateTime Alta = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

    private static ProductoDto P(int id, string codigo, string nombre, decimal costo, decimal stock, string unidad, string? categoria,
        bool activo = true) =>
        new(id, codigo, null, nombre, null, categoria is null ? null : 1, categoria, null, 1, unidad, costo, stock, 0m, activo, Alta);

    /// <summary>Los productos del mockup de Productos.</summary>
    internal static List<ProductoDto> Productos() =>
    [
        P(1, "MAT-0142", "Cemento portland 50 kg", 480m, 320m, "Bolsa", "Construcción"),
        P(2, "MAT-0198", "Arena gruesa", 890m, 45m, "Metro cúbico", "Construcción"),
        P(3, "PLO-0231", "Caño PVC 110 mm", 650m, 12m, "Unidad", "Plomería"),
        P(4, "PIN-0305", "Pintura látex blanca 20 L", 2340m, 6m, "Balde", "Pintura"),
        P(5, "SEG-0356", "Guantes de trabajo", 95m, 180m, "Par", "Seguridad"),
        P(6, "ELE-0402", "Lámpara LED 30 W", 410m, 8m, "Unidad", "Electricidad"),
        P(7, "ELE-0418", "Cable 2x1,5 mm", 38m, 1250m, "Metro", "Electricidad"),
        P(8, "LIM-0455", "Hipoclorito de sodio 5 L", 210m, -3m, "Bidón", "Limpieza"),
        P(9, "LIM-0470", "Bolsas de residuo 80x110", 175m, 60m, "Paquete", "Limpieza"),
        P(10, "HER-0512", "Pala de punta", 1150m, 14m, "Unidad", null),
        P(11, "VIA-0533", "Pintura vial amarilla 4 L", 1890.5m, 22.5m, "Lata", "Pintura"),
        P(12, "HER-0560", "Carretilla reforzada", 3200m, 0m, "Unidad", "Herramientas", activo: false),
    ];

    /// <summary>Los mismos, con la unidad de "Arena gruesa" más larga que el tope.</summary>
    internal static List<ProductoDto> ConCeldaCortada() =>
        Productos().Select(p => p.Nombre == "Arena gruesa" ? p with { UnidadMedidaNombre = UnidadLarga } : p).ToList();

    private sealed record Montaje(Window Ventana, DataGrid Grilla);

    private static DataGridTemplateColumn Columna(string header, DataGridLength ancho, string ruta, IValueConverter? conversor = null) => new()
    {
        Header = header,
        Width = ancho,
        SortMemberPath = ruta,
        CellTemplate = new FuncDataTemplate<ProductoDto>((_, _) =>
        {
            var tb = new TextBlock
            {
                [!TextBlock.TextProperty] = new Binding(ruta) { Converter = conversor },
                [!ToolTip.TipProperty] = new Binding(ruta) { Converter = conversor },
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            TooltipSiSeCorta.SetActivo(tb, true);
            return tb;
        }),
    };

    private static Montaje Montar(IEnumerable<ProductoDto> items, double anchoGrilla, double alto = 760)
    {
        var codigo = Columna("CÓDIGO", DataGridLength.Auto, "Codigo");
        ColumnaAuto.SetMedir(codigo, true);
        var nombre = Columna("NOMBRE", new DataGridLength(2, DataGridLengthUnitType.Star), "Nombre");
        var costo = Columna("COSTO", DataGridLength.Auto, "PrecioCosto", MonedaConverter.Instance);
        costo.MinWidth = 100;
        costo.CellStyleClasses.Add("num");
        ColumnaAuto.SetMedir(costo, true);
        ColumnaAuto.SetConversor(costo, MonedaConverter.Instance);
        var stock = Columna("STOCK", new DataGridLength(220), "StockActual", CantidadConverter.Instance);
        stock.CellStyleClasses.Add("num");
        var unidad = Columna("UNIDAD", DataGridLength.Auto, "UnidadMedidaNombre");
        ColumnaAuto.SetMedir(unidad, true);
        var categoria = Columna("CATEGORÍA", new DataGridLength(1, DataGridLengthUnitType.Star), "CategoriaNombre");
        var estado = Columna("ESTADO", new DataGridLength(78), "Activo");
        estado.CanUserSort = false;

        var grilla = new DataGrid
        {
            IsReadOnly = true, CanUserSortColumns = true, CanUserResizeColumns = true,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(grilla, ScrollBarVisibility.Auto);
        foreach (var c in new DataGridColumn[] { codigo, nombre, costo, stock, unidad, categoria, estado })
            grilla.Columns.Add(c);
        grilla.ItemsSource = new DataGridCollectionView(new ObservableCollection<ProductoDto>(items));

        var ventana = new Window { Width = anchoGrilla, Height = alto, Content = new UserControl { Classes = { "ui2" }, Content = grilla } };
        ventana.Show();
        Pasadas();
        return new Montaje(ventana, grilla);
    }

    private static void Pasadas()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridColumn Col(DataGrid grilla, string header) => grilla.Columns.Single(c => Equals(c.Header, header));

    private static double[] Anchos(DataGrid grilla) => grilla.Columns.Select(c => c.ActualWidth).ToArray();

    private static string Describir(DataGrid grilla) =>
        string.Join(", ", grilla.Columns.Select(c => $"{c.Header}={c.ActualWidth:F1}"));

    /// <summary>El ancho contra el que la grilla estira las * y decide el scroll horizontal (CellsWidth, interno).</summary>
    private static double AnchoInterno(DataGrid grilla) =>
        (double)typeof(DataGrid).GetProperty("CellsWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grilla)!;

    private static bool HayScrollHorizontal(DataGrid grilla) =>
        grilla.GetVisualDescendants().OfType<ScrollBar>().Any(s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);

    private static TextBlock CeldaBajoHeader(DataGrid grilla, object item, string titulo)
    {
        var header = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, titulo));
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
        var header = m.Grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, titulo));
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseDown(centro, MouseButton.Left);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        Pasadas();
    }

    [AvaloniaTheory]
    [InlineData(Grilla1280)]
    [InlineData(Grilla1366)]
    public void UnidadMasLargaQueElEspacioLibre_LasColumnasEntranEnLaGrillaYNoHayScrollHorizontal(double ancho)
    {
        var m = Montar(ConCeldaCortada(), ancho);
        var interno = AnchoInterno(m.Grilla);
        TestContext.Current.TestOutputHelper?.WriteLine($"grilla {ancho} (interno {interno:F1}): {Describir(m.Grilla)}");

        var suma = m.Grilla.Columns.Sum(c => c.ActualWidth);
        Assert.True(suma <= interno + 0.5, $"Las columnas suman {suma:F1} y la grilla tiene {interno:F1} por dentro: {Describir(m.Grilla)}");
        Assert.False(HayScrollHorizontal(m.Grilla), $"Hay scroll horizontal: {Describir(m.Grilla)}");
        // Las * quedan en su piso y la unidad toma lo que sobra: no se achica más de lo necesario.
        Assert.Equal(DataGridColumnasAutoBehavior.PisoProporcionalPorDefecto, Col(m.Grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(DataGridColumnasAutoBehavior.PisoProporcionalPorDefecto, Col(m.Grilla, "CATEGORÍA").ActualWidth, 1);
        Assert.True(suma >= interno - 1, $"Sobra lugar: suma {suma:F1} con {interno:F1} por dentro: {Describir(m.Grilla)}");
        Assert.True(Col(m.Grilla, "UNIDAD").ActualWidth < ColumnaAuto.TopePorDefecto,
            $"precondición: a este ancho la unidad no llega al tope ({Describir(m.Grilla)})");
    }

    [AvaloniaTheory]
    [InlineData(Grilla1280)]
    [InlineData(Grilla1366)]
    public void UnidadAchicadaPorElEspacio_LaCeldaSeCortaConElipsis_YElTooltipAbre(double ancho)
    {
        var items = ConCeldaCortada();
        var m = Montar(items, ancho);
        var arena = items.Single(p => p.Nombre == "Arena gruesa");

        var cortada = CeldaBajoHeader(m.Grilla, arena, "UNIDAD");
        Assert.Equal(UnidadLarga, cortada.Text);
        Assert.True(TooltipSiSeCorta.EstaCortado(cortada), $"La unidad no se cortó: {Describir(m.Grilla)}");
        Assert.Contains(cortada.TextLayout.TextLines, l => l.HasCollapsed);
        Assert.False(TooltipSiSeCortaTests.SeCancela(cortada), "La celda cortada no abre el tooltip.");
        Assert.Equal(UnidadLarga, ToolTip.GetTip(cortada));

        var entera = CeldaBajoHeader(m.Grilla, items[0], "UNIDAD");
        Assert.False(TooltipSiSeCorta.EstaCortado(entera));
        Assert.True(TooltipSiSeCortaTests.SeCancela(entera), "Una unidad que entra abre el tooltip.");
    }

    [AvaloniaFact]
    public void AlAgrandarLaVentana_UnidadCreceHastaElTopeYNoLoPasa()
    {
        var m = Montar(ConCeldaCortada(), Grilla1280);
        var unidad = Col(m.Grilla, "UNIDAD");
        var anterior = unidad.ActualWidth;

        foreach (var ancho in new[] { Grilla1366, 1100.0, 1200, 1400, 1800 })
        {
            m.Ventana.Width = ancho;
            Pasadas();
            TestContext.Current.TestOutputHelper?.WriteLine($"grilla {ancho}: {Describir(m.Grilla)}");
            Assert.True(unidad.ActualWidth >= anterior - 0.5, $"A {ancho} UNIDAD bajó de {anterior:F1} a {unidad.ActualWidth:F1}.");
            Assert.True(unidad.ActualWidth <= ColumnaAuto.TopePorDefecto + 0.5, $"A {ancho} UNIDAD pasó el tope: {unidad.ActualWidth:F1}.");
            Assert.True(m.Grilla.Columns.Sum(c => c.ActualWidth) <= AnchoInterno(m.Grilla) + 0.5, $"A {ancho} no entra: {Describir(m.Grilla)}");
            // El reparto termina en equilibrio, no porque la guarda anti-loop se rindió.
            Assert.Equal(0, DataGridColumnasAutoBehavior.ReparacionesSeguidas(m.Grilla));
            anterior = unidad.ActualWidth;
        }
        Assert.Equal(ColumnaAuto.TopePorDefecto, unidad.ActualWidth, 1);

        // Y al volver a achicarla, vuelve a ceder.
        m.Ventana.Width = Grilla1280;
        Pasadas();
        Assert.True(m.Grilla.Columns.Sum(c => c.ActualWidth) <= AnchoInterno(m.Grilla) + 0.5, $"Al achicar no entra: {Describir(m.Grilla)}");
        Assert.False(HayScrollHorizontal(m.Grilla));
    }

    [AvaloniaFact]
    public void ScrollearYOrdenar_NoCambianLosAnchos()
    {
        // Muchas filas, con la unidad larga al final: la grilla virtualiza y la fila no está realizada.
        var items = ConCeldaCortada();
        items.AddRange(Enumerable.Range(100, 300).Select(i => P(i, $"MAT-{i:0000}", $"Producto {i}", 10m + i, i, "Unidad", "Construcción")));
        var larga = items[1];
        items.RemoveAt(1);
        items.Add(larga);
        var m = Montar(items, Grilla1280, alto: 500);
        var antes = Anchos(m.Grilla);
        Assert.False(HayScrollHorizontal(m.Grilla), $"precondición: sin scroll horizontal ({Describir(m.Grilla)})");

        m.Grilla.ScrollIntoView(larga, null);
        Pasadas();
        Assert.Contains(m.Grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, larga));
        Assert.Equal(antes, Anchos(m.Grilla));

        foreach (var titulo in new[] { "UNIDAD", "CÓDIGO", "COSTO", "UNIDAD" })
        {
            ClickEnHeader(m, titulo);
            Assert.Equal(antes, Anchos(m.Grilla));
        }
        Assert.Single(((DataGridCollectionView)m.Grilla.ItemsSource!).SortDescriptions);

        ((DataGridCollectionView)m.Grilla.ItemsSource!).Filter = o => ((ProductoDto)o).Id % 2 == 0;
        Pasadas();
        Assert.False(HayScrollHorizontal(m.Grilla), $"Al filtrar apareció scroll horizontal: {Describir(m.Grilla)}");
        Assert.Equal(antes, Anchos(m.Grilla));
    }

    [AvaloniaTheory]
    [InlineData(Grilla1280, 208)]
    [InlineData(Grilla1366, 294)]
    public void ConLosDatosDeSiempre_LosAnchosSonLosAprobados(double ancho, double nombre)
    {
        var m = Montar(Productos(), ancho);
        TestContext.Current.TestOutputHelper?.WriteLine($"grilla {ancho}: {Describir(m.Grilla)}");

        Assert.Equal(85, Col(m.Grilla, "CÓDIGO").ActualWidth, 1);
        Assert.Equal(nombre, Col(m.Grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(100, Col(m.Grilla, "COSTO").ActualWidth, 1);
        Assert.Equal(220, Col(m.Grilla, "STOCK").ActualWidth, 1);
        Assert.Equal(97, Col(m.Grilla, "UNIDAD").ActualWidth, 1);
        Assert.Equal(160, Col(m.Grilla, "CATEGORÍA").ActualWidth, 1);
        Assert.Equal(78, Col(m.Grilla, "ESTADO").ActualWidth, 1);
        Assert.False(HayScrollHorizontal(m.Grilla));
    }
    [AvaloniaFact]
    public void DosMedidasQueNoEntran_CedeLaMasAnchaPrimero_YLaQueYaEsAngostaQuedaEntera()
    {
        // CÓDIGO y UNIDAD largos, COSTO corto: se nivelan hacia abajo, así que COSTO (100) no se toca y las otras
        // dos quedan iguales, las dos cortadas.
        var items = ConCeldaCortada();
        items[0] = items[0] with { Codigo = "MAT-CONSTRUCCION-REFORZADA-0142" };
        var m = Montar(items, Grilla1280);
        TestContext.Current.TestOutputHelper?.WriteLine(Describir(m.Grilla));

        var codigo = Col(m.Grilla, "CÓDIGO").ActualWidth;
        var unidad = Col(m.Grilla, "UNIDAD").ActualWidth;
        Assert.Equal(100, Col(m.Grilla, "COSTO").ActualWidth, 1);
        Assert.True(Math.Abs(codigo - unidad) <= 1, $"No se nivelaron: {Describir(m.Grilla)}");
        Assert.True(TooltipSiSeCorta.EstaCortado(CeldaBajoHeader(m.Grilla, items[0], "CÓDIGO")), "El código largo no se cortó.");
        Assert.True(m.Grilla.Columns.Sum(c => c.ActualWidth) <= AnchoInterno(m.Grilla) + 0.5, $"No entra: {Describir(m.Grilla)}");
        Assert.False(HayScrollHorizontal(m.Grilla));
    }

    [AvaloniaFact]
    public void TanAngostaQueNiCediendoEntra_LasMedidasVuelvenASuAnchoMedido_LasEstrellasQuedanEnSuPiso_YHayScroll()
    {
        // Lo último que cede es el piso de las *: debajo, NOMBRE no se lee. Y si ni con las medidas en su título entra,
        // igual hay scroll: cortar una medida no evita nada, así que vuelven a su ancho medido (D5 de la 3c-2).
        var items = ConCeldaCortada();
        var m = Montar(items, 700);
        TestContext.Current.TestOutputHelper?.WriteLine(Describir(m.Grilla));

        Assert.Equal(DataGridColumnasAutoBehavior.PisoProporcionalPorDefecto, Col(m.Grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(DataGridColumnasAutoBehavior.PisoProporcionalPorDefecto, Col(m.Grilla, "CATEGORÍA").ActualWidth, 1);
        foreach (var titulo in new[] { "CÓDIGO", "COSTO", "UNIDAD" })
        {
            var col = Col(m.Grilla, titulo);
            var medido = DataGridColumnasAutoBehavior.AnchoMedido(m.Grilla, col);
            Assert.True(medido is not null, $"{titulo} no se midió");
            Assert.Equal(medido!.Value, col.ActualWidth, 1);
        }
        // Todos los códigos, así está incluido el más largo (el que fija el ancho medido).
        foreach (var item in items)
            Assert.False(TooltipSiSeCorta.EstaCortado(CeldaBajoHeader(m.Grilla, item, "CÓDIGO")),
                $"Con scroll, una columna medida no se corta ({item.Codigo}): {Describir(m.Grilla)}");
        Assert.True(HayScrollHorizontal(m.Grilla), $"Sin scroll con las columnas desbordando: {Describir(m.Grilla)}");
    }

    [AvaloniaFact]
    public void SinColumnasProporcionales_LaMedidaTambienCedeYNoHayScroll()
    {
        var codigo = Columna("CÓDIGO", DataGridLength.Auto, "Codigo");
        ColumnaAuto.SetMedir(codigo, true);
        var unidad = Columna("UNIDAD", DataGridLength.Auto, "UnidadMedidaNombre");
        ColumnaAuto.SetMedir(unidad, true);
        var grilla = new DataGrid { IsReadOnly = true, CanUserResizeColumns = true };
        grilla.Columns.Add(codigo);
        grilla.Columns.Add(new DataGridTextColumn { Header = "STOCK", Width = new DataGridLength(220), Binding = new Binding("StockActual") });
        grilla.Columns.Add(unidad);
        grilla.ItemsSource = new DataGridCollectionView(new ObservableCollection<ProductoDto>(ConCeldaCortada()));
        var ventana = new Window { Width = 450, Height = 500, Content = new UserControl { Classes = { "ui2" }, Content = grilla } };
        ventana.Show();
        Pasadas();
        TestContext.Current.TestOutputHelper?.WriteLine(Describir(grilla));

        Assert.True(unidad.ActualWidth < ColumnaAuto.TopePorDefecto, $"UNIDAD no cedió: {Describir(grilla)}");
        Assert.True(grilla.Columns.Sum(c => c.ActualWidth) <= AnchoInterno(grilla) + 0.5, $"No entra: {Describir(grilla)}");
        Assert.False(HayScrollHorizontal(grilla), $"Hay scroll horizontal: {Describir(grilla)}");
    }
    // Pin de comportamiento: expresa la intencion de la regla defensiva (sin columnas proporcionales solo se asignan las medidas).
    // Ninguna mutacion de esa regla lo pone en rojo; no custodia, documenta.
    [AvaloniaFact]
    public void SinColumnasProporcionales_LasAutoQueNoSeMidenSiguenCreciendoSolasAlScrollear()
    {
        // Sin *, el reparto solo toca las medidas: una Auto sin medir crece como siempre cuando aparece una fila
        // más ancha al scrollear, y la medida le cede el lugar.
        var codigo = Columna("CÓDIGO", DataGridLength.Auto, "Codigo");
        ColumnaAuto.SetMedir(codigo, true);
        var unidad = Columna("UNIDAD", DataGridLength.Auto, "UnidadMedidaNombre");
        ColumnaAuto.SetMedir(unidad, true);
        var categoria = Columna("CATEGORÍA", DataGridLength.Auto, "CategoriaNombre");
        var items = ConCeldaCortada();
        items.AddRange(Enumerable.Range(100, 200).Select(i => P(i, $"MAT-{i:0000}", $"Producto {i}", 10m, i, "Unidad", "Pintura")));
        var ancha = P(999, "MAT-0999", "Último", 10m, 1, "Unidad", "Construcción y mantenimiento");
        items.Add(ancha);
        var grilla = new DataGrid { IsReadOnly = true, CanUserResizeColumns = true, Height = 300 };
        grilla.Columns.Add(codigo);
        grilla.Columns.Add(new DataGridTextColumn { Header = "STOCK", Width = new DataGridLength(220), Binding = new Binding("StockActual") });
        grilla.Columns.Add(unidad);
        grilla.Columns.Add(categoria);
        grilla.ItemsSource = new DataGridCollectionView(new ObservableCollection<ProductoDto>(items));
        var ventana = new Window { Width = 700, Height = 400, Content = new UserControl { Classes = { "ui2" }, Content = grilla } };
        ventana.Show();
        Pasadas();
        var antes = categoria.ActualWidth;

        grilla.ScrollIntoView(ancha, null);
        Pasadas();
        TestContext.Current.TestOutputHelper?.WriteLine($"antes CATEGORÍA={antes:F1}; después {Describir(grilla)} reparaciones={DataGridColumnasAutoBehavior.ReparacionesSeguidas(grilla)}");

        Assert.True(categoria.ActualWidth > antes + 10, $"CATEGORÍA no creció: {antes:F1} -> {Describir(grilla)}");
        Assert.True(categoria.Width.IsAuto, $"CATEGORÍA dejó de ser Auto: {categoria.Width}");
        Assert.True(grilla.Columns.Sum(c => c.ActualWidth) <= AnchoInterno(grilla) + 0.5, $"No entra: {Describir(grilla)}");
    }

    // Pin de comportamiento: expresa la intencion de la clausula Width.Value del equilibrio.
    // Ninguna mutacion de esa regla lo pone en rojo; no custodia, documenta.
    [AvaloniaFact]
    public void RecargaConUnaUnidadMasLarga_NingunaPasadaDeLayoutDesborda()
    {
        // La medición nueva asigna el ancho medido en píxeles y en la misma pasada el reparto lo vuelve a ceder: la
        // grilla no llega a medirse nunca con la columna en su ancho medido.
        var items = ConCeldaCortada();
        var m = Montar(items, Grilla1280);
        var maximo = 0.0;
        void AlActualizar(object? s, EventArgs e) => maximo = Math.Max(maximo, m.Grilla.Columns.Sum(c => c.ActualWidth) - AnchoInterno(m.Grilla));
        m.Grilla.LayoutUpdated += AlActualizar;

        var fuente = (ObservableCollection<ProductoDto>)((DataGridCollectionView)m.Grilla.ItemsSource!).SourceCollection;
        fuente.Add(P(50, "MAT-0050", "Ripio", 10m, 1, UnidadLarga + " y descarga", "Construcción"));
        Pasadas();
        m.Grilla.LayoutUpdated -= AlActualizar;

        Assert.True(maximo <= 0.5, $"Una pasada desbordó {maximo:F1} px: {Describir(m.Grilla)}");
        Assert.Equal(0, DataGridColumnasAutoBehavior.ReparacionesSeguidas(m.Grilla));
    }
    [AvaloniaFact]
    public void UsuarioArrastraUnaColumnaQueCedio_NiRecargarNiCambiarElAnchoLaPisan()
    {
        var m = Montar(ConCeldaCortada(), Grilla1366);
        var unidad = Col(m.Grilla, "UNIDAD");
        Assert.Equal(231, unidad.ActualWidth, 1);

        // Achicarla 60 px arrastrando el separador derecho de su header.
        var header = m.Grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "UNIDAD"));
        var borde = header.TranslatePoint(new Point(header.Bounds.Width - 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseMove(borde);
        m.Ventana.MouseDown(borde, MouseButton.Left);
        for (var paso = 1; paso <= 5; paso++)
        {
            m.Ventana.MouseMove(borde - new Point(60 * paso / 5.0, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }
        m.Ventana.MouseUp(borde - new Point(60, 0), MouseButton.Left);
        Pasadas();
        var arrastrado = unidad.ActualWidth;
        Assert.True(arrastrado < 231 - 30, $"precondición: el arrastre la achicó ({Describir(m.Grilla)})");

        var fuente = (ObservableCollection<ProductoDto>)((DataGridCollectionView)m.Grilla.ItemsSource!).SourceCollection;
        fuente.Add(P(50, "MAT-0050", "Ripio", 10m, 1, UnidadLarga + " y descarga", "Construcción"));
        Pasadas();
        Assert.Equal(arrastrado, unidad.ActualWidth, 1);

        m.Ventana.Width = Grilla1280;
        Pasadas();
        Assert.Equal(arrastrado, unidad.ActualWidth, 1);
    }
}
