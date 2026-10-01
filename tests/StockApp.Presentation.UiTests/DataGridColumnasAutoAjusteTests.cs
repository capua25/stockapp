using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Movimientos;
using StockApp.Presentation.Views.Reportes;
using Xunit;
using static StockApp.Presentation.UiTests.GrillaStockHelpers;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardianes de <see cref="DataGridColumnasAutoBehavior"/>: en las grillas que mezclan columnas
/// proporcionales (<c>*</c>) con columnas <c>Auto</c> y tienen el redimensionado habilitado, la
/// grilla se arma vacía y los datos llegan después. DataGrid 12.0.1 procesa el crecimiento de una
/// columna Auto como un resize (<c>DataGridColumn.Resize(userInitiated: false)</c>): solo le saca
/// ancho a las columnas a su DERECHA (y si no alcanza, aprieta a las de ancho fijo hasta su
/// MinWidth). Resultado: "Operador de Dep" cortado en Usuario, "P. Costo" cortado, y la columna
/// Stock de 220 dibujada en ~198.
///
/// Los anchos de ventana (1100/1300) son los de la vista real dentro del shell (ventana menos el
/// sidebar), no los 1700 de otros guardianes: a 1700 sobra espacio a la derecha y el bug no se ve.
///
/// El invariante se mide por el EFECTO (texto natural vs. ancho arreglado del TextBlock, ancho
/// efectivo de la columna fija), no por la propiedad que lo pide.
/// </summary>
public class DataGridColumnasAutoAjusteTests
{
    // ── Datos ──────────────────────────────────────────────────────────────────────────────────

    private static MovimientoHistorialDto Movimiento(int i, string usuario = "admin", string? comentario = "Reposición mensual") => new(
        MovimientoId: i, ProductoId: i, ProductoNombre: $"Producto de prueba {i}",
        Tipo: i % 3 == 0 ? TipoMovimiento.Salida : TipoMovimiento.Entrada,
        Motivo: i % 2 == 0 ? MotivoMovimiento.Compra : MotivoMovimiento.Ajuste,
        Cantidad: 10 + i, PrecioUnitario: 100 + i, StockAnterior: 5m, StockNuevo: 15m + i,
        Comentario: comentario, Fecha: new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc),
        UsuarioId: 1, UsuarioNombre: usuario);

    /// <summary>Historial con el usuario largo de la app real ("Operador de Depósito ...").</summary>
    private static List<MovimientoHistorialDto> HistorialReal(int cantidad = 30) =>
        Enumerable.Range(0, cantidad)
            .Select(i => Movimiento(i, i % 4 == 0 ? "Operador de Depósito Central" : "admin"))
            .ToList();

    private static List<ValorizacionItemDto> ValorizacionReal() =>
        Enumerable.Range(0, 30)
            .Select(i => new ValorizacionItemDto(i, $"C{i:000}", $"Producto valorizado {i}", "Ferretería",
                StockActual: 10 + i, PrecioCosto: 1234567.89m + i, ValorCosto: 98765432.10m + i))
            .ToList();

    private static ProductoDto Producto(int id, decimal stock) => new(
        Id: id, Codigo: $"PRD-{id:00000}", CodigoBarras: null, Nombre: $"Producto {id}", Descripcion: null,
        CategoriaId: null, CategoriaNombre: "Materiales de construcción", ProveedorId: null, UnidadMedidaId: 1,
        UnidadMedidaNombre: "Metro cuadrado", PrecioCosto: 1234567.89m + id, StockActual: stock,
        StockMinimo: 0m, Activo: true, FechaAlta: DateTime.UtcNow);

    // ── Montaje y medición ─────────────────────────────────────────────────────────────────────

    private static (Window Window, DataGrid Grid) Montar(Control vista, double ancho, bool conBehavior = true)
    {
        // Valor local antes de mostrar: le gana al Setter del tema desde la primera pasada.
        var grid = vista.GetLogicalDescendants().OfType<DataGrid>().Single();
        if (!conBehavior)
            DataGridColumnasAutoBehavior.SetAjustarColumnasAuto(grid, false);
        var window = new Window { Width = ancho, Height = 900, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, grid);
    }

    private static ScrollBar? ScrollHorizontal(DataGrid grid) =>
        grid.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(s => s.Orientation == Avalonia.Layout.Orientation.Horizontal);

    private static void Cargar(DataGrid grid, System.Collections.IEnumerable items)
    {
        grid.ItemsSource = new DataGridCollectionView(items);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridColumn Columna(DataGrid grid, string header) =>
        grid.Columns.Single(c => Equals(c.Header, header));

    /// <summary>Ancho natural del texto: un TextBlock gemelo medido sin restricción.</summary>
    private static double AnchoNatural(TextBlock tb)
    {
        var gemelo = new TextBlock
        {
            Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight, FontStyle = tb.FontStyle,
            FontFamily = tb.FontFamily, LetterSpacing = tb.LetterSpacing, FontFeatures = tb.FontFeatures,
        };
        gemelo.Measure(Size.Infinity);
        return gemelo.DesiredSize.Width;
    }

    private static bool ContieneX(Visual v, Window window, double x)
    {
        var x0 = v.TranslatePoint(new Point(0, 0), window)!.Value.X;
        return x >= x0 && x <= x0 + v.Bounds.Width;
    }

    /// <summary>
    /// Textos de la columna (encabezado + celdas realizadas) cuyo TextBlock quedó arreglado más
    /// angosto que su ancho natural, o sea, recortados.
    /// </summary>
    private static List<string> TextosRecortados(DataGrid grid, Window window, string header)
    {
        var headerCell = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, header));
        var centroX = headerCell.TranslatePoint(new Point(headerCell.Bounds.Width / 2, 0), window)!.Value.X;

        var textos = headerCell.GetVisualDescendants().OfType<TextBlock>()
            .Concat(grid.GetVisualDescendants().OfType<DataGridCell>()
                .Where(c => c.IsEffectivelyVisible && ContieneX(c, window, centroX))
                .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>()))
            .Where(tb => tb.IsEffectivelyVisible && !string.IsNullOrEmpty(tb.Text));

        return textos
            .Select(tb => (tb.Text, Natural: AnchoNatural(tb), Arreglado: tb.Bounds.Width))
            .Where(t => t.Arreglado < t.Natural - 0.5)
            .Select(t => $"'{t.Text}' mide {t.Arreglado:F1}px y necesita {t.Natural:F1}px")
            .ToList();
    }

    private static void AssertSinRecorte(DataGrid grid, Window window, string etapa, params string[] headers)
    {
        var fallas = headers
            .SelectMany(h => TextosRecortados(grid, window, h).Select(t => $"{h}: {t}"))
            .ToList();
        Assert.True(fallas.Count == 0, $"[{etapa}] textos recortados: {string.Join("; ", fallas)}");
    }

    private static Dictionary<string, double> Anchos(DataGrid grid) =>
        grid.Columns.Where(c => c.IsVisible).ToDictionary(c => c.Header?.ToString() ?? "", c => c.ActualWidth);

    private static string Describir(Dictionary<string, double> anchos) =>
        string.Join(", ", anchos.Select(kv => $"{kv.Key}={kv.Value:F1}"));

    /// <summary>Arrastra el separador derecho del encabezado <paramref name="header"/> <paramref name="dx"/> píxeles.</summary>
    private static void ArrastrarSeparador(Window window, DataGrid grid, string header, double dx)
    {
        var headerCell = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, header));
        var borde = headerCell.TranslatePoint(new Point(headerCell.Bounds.Width - 2, headerCell.Bounds.Height / 2), window)!.Value;
        window.MouseMove(borde);
        window.MouseDown(borde, MouseButton.Left);
        for (var paso = 1; paso <= 5; paso++)
        {
            window.MouseMove(borde + new Point(dx * paso / 5, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }
        window.MouseUp(borde + new Point(dx, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // ── Tema ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Tema_AplicaElBehaviorATodaDataGrid_SinDeclararloEnLaVista()
    {
        var grid = new DataGrid();
        var window = new Window { Width = 600, Height = 400, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(DataGridColumnasAutoBehavior.GetAjustarColumnasAuto(grid),
            "Themes/DataGrid.axaml tiene que activar el behavior en TODAS las DataGrid, así las grillas nuevas lo heredan.");
    }

    // ── Vistas reales ──────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(1100)]
    [InlineData(1300)]
    [InlineData(1500)]
    public void HistorialMovimientos_AlLlegarLosDatos_UsuarioYTipoNoQuedanRecortados(double ancho)
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), ancho);
        Cargar(grid, HistorialReal());

        AssertSinRecorte(grid, window, "carga", "Usuario", "Tipo", "Fecha", "Motivo", "Cantidad", "Precio");
        Assert.Equal(220, Columna(grid, "Stock nuevo").ActualWidth, 1);
    }

    [AvaloniaTheory]
    [InlineData(1100)]
    [InlineData(1300)]
    public void Valorizacion_AlLlegarLosDatos_PCostoYValorCostoNoQuedanRecortados(double ancho)
    {
        var (window, grid) = Montar(new ValorizacionView(), ancho);
        Cargar(grid, ValorizacionReal());

        AssertSinRecorte(grid, window, "carga", "P. Costo", "Valor Costo", "Código");
        Assert.Equal(220, Columna(grid, "Stock").ActualWidth, 1);
    }

    [AvaloniaTheory]
    [InlineData(1100)]
    [InlineData(1300)]
    public void Productos_AlLlegarLosDatos_LaColumnaStockMideEfectivamente220(double ancho)
    {
        var (window, grid) = Montar(new ProductoListView(), ancho);
        Cargar(grid, Enumerable.Range(1, 30).Select(i => Producto(i, i == 3 ? -99999.5m : i)).ToList());

        Assert.Equal(220, Columna(grid, "Stock").ActualWidth, 1);
        AssertSinRecorte(grid, window, "carga", "Código", "Costo", "Unidad");
        AssertBadgeNoPisaElNumero(grid, window, "Stock", "carga");
    }

    // ── (a) Respeta el redimensionado manual ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_ColumnaArrastradaPorElUsuario_ConservaSuAnchoEnRecargasPosteriores()
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), 1300);
        Cargar(grid, HistorialReal());

        // El usuario agranda la columna proporcional "Producto": eso aprieta a las de su derecha.
        ArrastrarSeparador(window, grid, "Producto", 160);
        var despuesDelArrastre = Anchos(grid);
        Assert.True(despuesDelArrastre["Producto"] > 0, "precondición");

        // Recarga (refresco) con los mismos datos y con datos más anchos.
        Cargar(grid, HistorialReal());
        Assert.Equal(despuesDelArrastre["Producto"], Columna(grid, "Producto").ActualWidth, 1);

        Cargar(grid, HistorialReal().Select(m => m with { UsuarioNombre = "Encargada de Depósito Municipal Norte" }).ToList());
        Assert.Equal(despuesDelArrastre["Producto"], Columna(grid, "Producto").ActualWidth, 1);
    }

    [AvaloniaFact]
    public void A_ColumnaAutoArrastradaMasAngosta_NoVuelveASuAnchoNaturalAlRecargar()
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), 1300);
        Cargar(grid, HistorialReal());
        var usuarioNatural = Columna(grid, "Usuario").ActualWidth;

        ArrastrarSeparador(window, grid, "Usuario", -60);
        var usuarioArrastrado = Columna(grid, "Usuario").ActualWidth;
        Assert.True(usuarioArrastrado < usuarioNatural - 30, $"precondición: el arrastre achicó Usuario ({usuarioNatural:F1} -> {usuarioArrastrado:F1})");

        Cargar(grid, HistorialReal());
        Assert.Equal(usuarioArrastrado, Columna(grid, "Usuario").ActualWidth, 1);
    }

    // ── (b) Sin loops: si no entra, resultado estable ──────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(700)]
    [InlineData(900)]
    [InlineData(1000)]
    public void B_VentanaAngosta_ElResultadoEsEstableYNoAprietaColumnasMasQueSinBehavior(double ancho)
    {
        // Referencia: la MISMA grilla sin el behavior (lo que hace DataGrid 12.0.1 solo).
        var (windowRef, gridRef) = Montar(new MovimientoHistorialView(), ancho, conBehavior: false);
        Cargar(gridRef, HistorialReal());
        var sinBehavior = Anchos(gridRef);
        windowRef.Close();

        var (window, grid) = Montar(new MovimientoHistorialView(), ancho);
        Cargar(grid, HistorialReal());
        var primera = Anchos(grid);

        // Estable: más pasadas de layout y otra recarga no mueven nada.
        for (var i = 0; i < 5; i++)
        {
            grid.InvalidateMeasure();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Anchos(grid).All(kv => Math.Abs(kv.Value - primera[kv.Key]) < 0.5),
                $"oscila en la pasada {i}: antes {Describir(primera)} / ahora {Describir(Anchos(grid))}");
        }

        // El behavior solo toma espacio de las columnas proporcionales: ninguna columna NO
        // proporcional queda más angosta que como la deja la grilla sin behavior.
        var apretadas = grid.Columns.Where(c => c.IsVisible && !c.Width.IsStar)
            .Where(c => c.ActualWidth < sinBehavior[c.Header!.ToString()!] - 0.5)
            .Select(c => $"{c.Header}: {sinBehavior[c.Header!.ToString()!]:F1} -> {c.ActualWidth:F1}")
            .ToList();
        Assert.True(apretadas.Count == 0,
            $"el behavior apretó columnas: {string.Join("; ", apretadas)}. Sin behavior: {Describir(sinBehavior)} / con: {Describir(primera)}");

        // Lo que no entra se ve con el scroll horizontal: las Auto quedan enteras y la fija en 220.
        Assert.True(ScrollHorizontal(grid) is { IsVisible: true }, "no entra todo: tiene que aparecer el scroll horizontal");
        AssertSinRecorte(grid, window, "desborde", "Usuario", "Tipo", "Fecha", "Motivo", "Cantidad", "Precio");
        Assert.Equal(220, Columna(grid, "Stock nuevo").ActualWidth, 1);
    }

    [AvaloniaFact(Timeout = 60000)]
    public void B_SiAlgoExternoPeleaConElReparto_ElBehaviorSeRindeEnVezDeColgarLaUi()
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), 1500);
        Cargar(grid, HistorialReal());

        // Un actor externo que, después de cada pasada de layout, le vuelve a poner otro peso a
        // "Producto". El behavior lo corrige, el actor lo pisa, y así. Sin la guarda, eso es un
        // loop de layout infinito (Avalonia corta con "Infinite layout loop detected" o la UI se
        // cuelga); con la guarda, el behavior se rinde y la grilla queda quieta.
        var producto = Columna(grid, "Producto");
        var pisadas = 0;
        grid.LayoutUpdated += (_, _) =>
        {
            if (pisadas++ < 10_000)
                producto.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        };
        grid.InvalidateMeasure();
        Dispatcher.UIThread.RunJobs();

        Assert.True(pisadas < 100, $"la pelea no terminó: {pisadas} pasadas de layout");
    }

    [AvaloniaFact]
    public void B_VentanaQueSeAchicaYSeAgranda_NoOscilaYVuelveACalzar()
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), 1300);
        Cargar(grid, HistorialReal());

        foreach (var ancho in new double[] { 1000, 800, 700, 900, 1300 })
        {
            window.Width = ancho;
            Dispatcher.UIThread.RunJobs();
            var antes = Anchos(grid);
            grid.InvalidateMeasure();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Anchos(grid).All(kv => Math.Abs(kv.Value - antes[kv.Key]) < 0.5),
                $"oscila a {ancho}px: {Describir(antes)} / {Describir(Anchos(grid))}");
        }

        AssertSinRecorte(grid, window, "de vuelta a 1300", "Usuario", "Tipo");
        Assert.Equal(220, Columna(grid, "Stock nuevo").ActualWidth, 1);
    }

    // ── (c) Filas que aparecen al hacer scroll ─────────────────────────────────────────────────

    [AvaloniaFact]
    public void C_FilaMasAnchaQueApareceAlScrollear_HaceCrecerLaColumnaSinApretarLasFijas()
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), 1300);
        var items = HistorialReal(300);
        var ancha = items[250] = Movimiento(250, "Encargada de Depósito Municipal Norte");
        Cargar(grid, items);

        Assert.DoesNotContain(grid.GetVisualDescendants().OfType<TextBlock>(), tb => tb.Text == ancha.UsuarioNombre);

        grid.ScrollIntoView(ancha, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), tb => tb.Text == ancha.UsuarioNombre && tb.IsEffectivelyVisible);
        AssertSinRecorte(grid, window, "tras scroll", "Usuario", "Tipo", "Fecha");
        Assert.Equal(220, Columna(grid, "Stock nuevo").ActualWidth, 1);
    }

    // ── (d) Recargas, filtro y ordenamiento: sin achicar ni saltar ─────────────────────────────

    [AvaloniaFact]
    public void D_OrdenarVariasVecesYFiltrar_NoAchicaColumnasNiHaceSaltarElLayout()
    {
        var (window, grid) = Montar(new MovimientoHistorialView(), 1300);
        Cargar(grid, HistorialReal());
        var inicial = Anchos(grid);

        foreach (var header in new[] { "Usuario", "Usuario", "Tipo", "Cantidad", "Fecha", "Usuario" })
        {
            ClickearHeader(window, grid, header);
            var ahora = Anchos(grid);
            Assert.True(ahora.All(kv => Math.Abs(kv.Value - inicial[kv.Key]) < 0.5),
                $"el layout saltó al ordenar por '{header}': {Describir(inicial)} / {Describir(ahora)}");
        }

        // Filtro: quedan solo filas con usuario corto. Las columnas no se achican de golpe.
        Cargar(grid, HistorialReal().Where(m => m.UsuarioNombre == "admin").ToList());
        var filtrado = Anchos(grid);
        Assert.True(filtrado.All(kv => kv.Value >= inicial[kv.Key] - 0.5),
            $"una recarga achicó columnas: {Describir(inicial)} / {Describir(filtrado)}");

        // Refresco con los datos completos: vuelve a lo mismo, sin saltos.
        Cargar(grid, HistorialReal());
        AssertSinRecorte(grid, window, "refresco", "Usuario", "Tipo");
    }

    // ── (e) Columnas * y fijas ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void E_ColumnasProporcionalesYFijas_NoSeRompen()
    {
        // 1500: todo entra (a 1300 el Historial ya desborda por 1px y pasa a scroll horizontal).
        var (window, grid) = Montar(new MovimientoHistorialView(), 1500);
        Cargar(grid, HistorialReal());

        Assert.Equal(220, Columna(grid, "Stock nuevo").ActualWidth, 1);

        // Las proporcionales llenan el resto y conservan la proporción 2:3 entre sí.
        var producto = Columna(grid, "Producto").ActualWidth;
        var comentario = Columna(grid, "Comentario").ActualWidth;
        Assert.True(producto > 40 && comentario > 40, $"Producto={producto:F1} Comentario={comentario:F1}");
        Assert.Equal(1.5, comentario / producto, 2);

        // Sin hueco ni desborde: las columnas ocupan exactamente el ancho de la grilla.
        Assert.True(ScrollHorizontal(grid) is not { IsVisible: true }, "no tiene que aparecer scroll horizontal cuando todo entra");
    }

    // ── (f) Rendimiento ────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void F_GrillaGrande_ElBehaviorNoAgregaCostoPerceptible()
    {
        static long Medir(bool conBehavior, List<MovimientoHistorialDto> items)
        {
            var (window, grid) = Montar(new MovimientoHistorialView(), 1300, conBehavior);
            var sw = Stopwatch.StartNew();
            Cargar(grid, items);
            for (var i = 0; i < 20; i++)
            {
                grid.ScrollIntoView(items[(i * 997) % items.Count], null);
                Dispatcher.UIThread.RunJobs();
            }
            sw.Stop();
            window.Close();
            return sw.ElapsedMilliseconds;
        }

        var items = Enumerable.Range(0, 5000)
            .Select(i => Movimiento(i, i % 7 == 0 ? $"Operador de Depósito {i}" : "admin"))
            .ToList();

        Medir(true, items); // calentamiento (JIT)
        var sin = Medir(false, items);
        var con = Medir(true, items);

        TestContext.Current.TestOutputHelper?.WriteLine($"5000 filas, carga + 20 saltos de scroll: sin behavior {sin} ms, con behavior {con} ms");
        Assert.True(con <= sin * 1.5 + 150, $"con behavior {con} ms vs sin behavior {sin} ms");
    }
}
