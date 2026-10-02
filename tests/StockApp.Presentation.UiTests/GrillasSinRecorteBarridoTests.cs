using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auditoria;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.Views.Movimientos;
using StockApp.Presentation.Views.Reportes;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Barrido: ninguna columna que NO sea proporcional (<c>Auto</c> o de ancho fijo) deja un header,
/// un texto o un botón recortado, en las Views reales montadas con datos de prueba.
///
/// Los anchos son los de la vista real dentro del shell: ventana menos el sidebar de 232px.
/// 1366 (la resolución típica de las PC municipales) -> 1134; 1920 -> 1688. Las columnas
/// proporcionales (<c>*</c>) quedan fuera a propósito: su MinWidth lo decide el usuario.
///
/// El invariante se mide por el EFECTO: el TextBlock arreglado más angosto que su ancho natural
/// (texto cortado) y el Button/control cuyo borde derecho se pasa del borde de su celda (la celda
/// recorta). No se mira ninguna propiedad de configuración.
/// </summary>
public class GrillasSinRecorteBarridoTests
{
    private const double SidebarPx = 232;

    private sealed record Escenario(string Nombre, Func<Control> Vista, Func<DataGrid, IEnumerable<object>> Datos);

    // ── Datos con los peores textos realistas ──────────────────────────────────────────────────

    private static MovimientoHistorialDto Movimiento(int i) => new(
        MovimientoId: i, ProductoId: i, ProductoNombre: $"Producto de prueba {i}",
        Tipo: i % 3 == 0 ? TipoMovimiento.Salida : TipoMovimiento.Entrada,
        Motivo: i % 2 == 0 ? MotivoMovimiento.Compra : MotivoMovimiento.Ajuste,
        Cantidad: 10 + i, PrecioUnitario: 1234567.89m + i, StockAnterior: -99999.5m, StockNuevo: 15m + i,
        Comentario: "Reposición mensual", Fecha: new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc),
        UsuarioId: 1, UsuarioNombre: i % 4 == 0 ? "Operador de Depósito Central" : "admin");

    private static ProductoDto Producto(int id, decimal stock) => new(
        Id: id, Codigo: $"PRD-{id:00000}", CodigoBarras: null, Nombre: $"Producto {id}", Descripcion: null,
        CategoriaId: null, CategoriaNombre: "Materiales de construcción", ProveedorId: null, UnidadMedidaId: 1,
        UnidadMedidaNombre: "Metro cuadrado", PrecioCosto: 1234567.89m + id, StockActual: stock,
        StockMinimo: 0m, Activo: true, FechaAlta: DateTime.UtcNow);

    private static IEnumerable<T> Muchos<T>(Func<int, T> fabrica, int n = 30) => Enumerable.Range(0, n).Select(fabrica);

    private static readonly Escenario[] Escenarios =
    {
        new("Valorización", () => new ValorizacionView(), _ => Muchos<object>(i =>
            new ValorizacionItemDto(i, $"C{i:000}", $"Producto valorizado {i}", "Ferretería",
                StockActual: i == 0 ? -99999.5m : 10 + i, PrecioCosto: 1234567.89m + i, ValorCosto: 98765432.10m + i))),
        new("Historial de movimientos", () => new MovimientoHistorialView(), _ => Muchos<object>(i => Movimiento(i))),
        new("Historial por producto", () => new HistorialPorProductoView(), _ => Muchos<object>(i => Movimiento(i))),
        new("Stock por categoría", () => new StockCategoriaView(), _ => Muchos<object>(i =>
            new StockCategoriaDto("Materiales de construcción", 1234, i == 0 ? -99999.5m : 8765.25m + i, 98765432.10m + i))),
        new("Más movidos", () => new MasMovidosView(), _ => Muchos<object>(i =>
            new MasMovidoDto(i, $"PRD-{i:00000}", $"Producto {i}", 12345 + i, 98765.5m + i))),
        new("Auditoría", () => new AuditoriaLogView(), _ => Muchos<object>(i =>
            new AuditoriaItemDto(new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc), "Operador de Depósito Central",
                i % 2 == 0 ? AccionAuditada.CambioPrecio : AccionAuditada.AltaProducto, "Producto", 1234567 + i, "detalle"))),
        new("Reporte de tareas", () => new ReporteTareasView(), _ => Muchos<object>(i =>
            new FilaReporteTareas("Dirección de Obras y Servicios Públicos", 12345, 12345, 12345, 12345, 49380))),
        new("Productos", () => new ProductoListView(), _ => Muchos<object>(i => Producto(i, i == 0 ? -99999.5m : 10 + i))),
        new("Ingreso por factura (renglones)", () => new IngresoPorFacturaView(), _ => Muchos<object>(i =>
            new FilaRenglonFacturaVm
            {
                Producto = Producto(i, 10), Cantidad = 12345.5m + i, PrecioUnitario = 1234567.89m + i,
                ActualizarPrecioCosto = i % 2 == 0,
            }, 8)),
        new("Zonas", () => new ZonaListView(), _ => Muchos<object>(i => new Zona { Id = i, Nombre = $"Zona costera número {i}", Activo = i % 2 == 0 })),
        new("Dimensiones temáticas", () => new DimensionTematicaListView(), _ => Muchos<object>(i => new DimensionTematica { Id = i, Nombre = $"Dimensión temática {i}", Activo = i % 2 == 0 })),
        new("Orígenes de financiamiento", () => new OrigenFinanciamientoListView(), _ => Muchos<object>(i => new OrigenFinanciamiento { Id = i, Nombre = $"Origen {i}", Activo = i % 2 == 0 })),
        new("Organismos responsables", () => new OrganismoResponsableListView(), _ => Muchos<object>(i => new OrganismoResponsable { Id = i, Nombre = $"Organismo {i}", Activo = i % 2 == 0 })),
        new("Unidades de medida", () => new UnidadMedidaListView(), _ => Muchos<object>(i => new UnidadMedida { Id = i, Nombre = $"Metro cuadrado {i}", Abreviatura = "m2", Activo = i % 2 == 0 })),
        new("Proveedores", () => new ProveedorListView(), _ => Muchos<object>(i => new Proveedor
            { Id = i, Nombre = $"Distribuidora Municipal del Este SRL {i}", Telefono = "099 123 456", Email = "contacto@distribuidora-este.com.uy", Direccion = "Av. Artigas 1234", Notas = "n", Activo = i % 2 == 0 })),
        new("Ingresos de caja", () => new IngresosView(), _ => Muchos<object>(i => new IngresoCaja
            { Id = i, Fecha = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), Concepto = "Transferencia de rentas generales", Monto = 98765432.10m + i,
              FuenteFinanciamiento = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales Departamentales", Activo = true }, Activo = i % 2 == 0 })),
        new("Control POA", () => new ControlPoaView(), _ => Muchos<object>(i =>
            new ControlPoaLineaDto(i, "Obras viales y mantenimiento urbano", "Programa de infraestructura", 2026,
                98765432.10m, 87654321.09m, -12345678.90m, 112.5m, i % 2 == 0))),
        new("Pagos de gasto", () => new PagosGastoView(), _ => Muchos<object>(i => new PagoGasto
            { Id = i, GastoId = 1, Fecha = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), Monto = 98765432.10m + i, Nota = "Pago parcial", Activo = i % 2 == 0 })),
        new("Gastos", () => new GastosView(), _ => Muchos<object>(i => new GastoFila(new Gasto
            {
                Id = i, Proveedor = new Proveedor { Id = 1, Nombre = "Distribuidora Municipal del Este SRL", Activo = true },
                NumeroFactura = "A-0004521", Detalle = "Compra de materiales de construcción para la rambla costanera",
                Fecha = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), MontoTotal = 98765432.10m + i,
                FuenteFinanciamiento = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales Departamentales", Activo = true },
                RubroGasto = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true },
                LineaPoa = new LineaPoa { Id = 1, Nombre = "Obras viales y mantenimiento urbano", Ejercicio = 2026, Activo = true },
                CondicionPago = CondicionPago.Contado, Activo = true,
            }, new DateTime(2026, 10, 1)))),
        new("Libro de caja (movimientos)", () => new LibroCajaView(), _ => Muchos<object>(i =>
            new MovimientoCajaDto(new DateOnly(2026, 3, 15), i % 2 == 0 ? "Ingreso" : "Egreso", "Compra de materiales de construcción",
                "Distribuidora Municipal del Este SRL", "A-0004521", "Rentas Generales Departamentales", "Materiales de construcción",
                98765432.10m, 12345678.90m, -98765432.10m))),
        new("Historial de importaciones", () => new HistorialImportacionesView(), _ => Muchos<object>(i =>
            new ImportacionHistorialDto(Guid.NewGuid(), new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc), 2026, "Operador de Depósito Central", i % 2 == 0))),
    };

    // ── Montaje ────────────────────────────────────────────────────────────────────────────────

    private static (Window Window, DataGrid Grid, HashSet<DataGridColumn> Proporcionales) Montar(Control vista, double ancho)
    {
        var grid = vista.GetLogicalDescendants().OfType<DataGrid>().First();
        // Antes del primer layout: DataGrid reasigna el Width de las columnas * cuando una Auto crece.
        var proporcionales = grid.Columns.Where(c => c.Width.IsStar).ToHashSet();
        var window = new Window { Width = ancho, Height = 900, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, grid, proporcionales);
    }

    private static void Cargar(DataGrid grid, IEnumerable<object> items)
    {
        var tipo = items.First().GetType();
        var lista = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tipo))!;
        foreach (var it in items) lista.Add(it);
        grid.ItemsSource = new DataGridCollectionView(lista);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    // ── Detección de recortes ──────────────────────────────────────────────────────────────────

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

    private static double X0(Visual v, Visual raiz) => v.TranslatePoint(new Point(0, 0), raiz)!.Value.X;

    /// <summary>
    /// Recortes de las columnas no proporcionales: TextBlocks (header y celdas) más angostos que
    /// su texto, y controles que se pasan del borde derecho de su celda o de su header.
    /// </summary>
    private static (List<string> Fallas, int ColumnasInspeccionadas) Recortes(
        DataGrid grid, Window window, HashSet<DataGridColumn> proporcionales)
    {
        var fallas = new List<string>();
        var inspeccionadas = 0;
        var headers = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Where(h => h.IsEffectivelyVisible && h.Bounds.Width > 0).ToList();
        var celdas = grid.GetVisualDescendants().OfType<DataGridCell>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0).ToList();

        foreach (var col in grid.Columns.Where(c => c.IsVisible && !proporcionales.Contains(c)))
        {
            var header = headers.FirstOrDefault(h => ReferenceEquals(HeaderDeColumna(grid, h), col));
            if (header is null) continue;
            inspeccionadas++;
            var nombre = col.Header?.ToString();
            if (string.IsNullOrEmpty(nombre))
                nombre = header.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
            if (string.IsNullOrEmpty(nombre)) nombre = $"(columna sin título #{col.DisplayIndex})";
            var centroX = X0(header, window) + header.Bounds.Width / 2;

            var textosHeader = header.GetVisualDescendants().OfType<TextBlock>()
                .Where(tb => tb.IsEffectivelyVisible && !string.IsNullOrEmpty(tb.Text));
            foreach (var tb in textosHeader)
                if (tb.Bounds.Width < AnchoNatural(tb) - 0.5)
                    fallas.Add($"{nombre} [header]: '{tb.Text}' mide {tb.Bounds.Width:F1}px y necesita {AnchoNatural(tb):F1}px");

            foreach (var celda in celdas.Where(c => X0(c, window) <= centroX && centroX <= X0(c, window) + c.Bounds.Width))
            {
                foreach (var tb in celda.GetVisualDescendants().OfType<TextBlock>()
                             .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)
                                         && t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap))
                {
                    if (tb.Bounds.Width < AnchoNatural(tb) - 0.5)
                        fallas.Add($"{nombre} [celda]: '{tb.Text}' mide {tb.Bounds.Width:F1}px y necesita {AnchoNatural(tb):F1}px");
                }

                var bordeCelda = X0(celda, window) + celda.Bounds.Width;
                foreach (var ctl in celda.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var derecha = X0(ctl, window) + ctl.Bounds.Width;
                    if (derecha > bordeCelda + 0.5)
                        fallas.Add($"{nombre} [celda]: botón '{ctl.Content}' termina en x={derecha:F0} y su celda en x={bordeCelda:F0}");
                    var interno = ctl.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text));
                    if (interno is not null && interno.Bounds.Width < AnchoNatural(interno) - 0.5)
                        fallas.Add($"{nombre} [celda]: el texto del botón '{ctl.Content}' mide {interno.Bounds.Width:F1}px y necesita {AnchoNatural(interno):F1}px");
                }
            }
        }
        return (fallas.Distinct().ToList(), inspeccionadas);
    }

    /// <summary>DataGridColumnHeader.OwningColumn es interno: se deduce por el índice visual del header.</summary>
    private static DataGridColumn? HeaderDeColumna(DataGrid grid, DataGridColumnHeader header)
    {
        var propio = header.GetType().GetProperty("OwningColumn",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return propio?.GetValue(header) as DataGridColumn;
    }

    // ── Barrido ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(1366)]
    [InlineData(1920)]
    public void ColumnasNoProporcionales_NoQuedanRecortadas_EnTodasLasVistas(double ventana)
    {
        var ancho = ventana - SidebarPx;
        var fallas = new List<string>();

        foreach (var e in Escenarios)
        {
            var (window, grid, proporcionales) = Montar(e.Vista(), ancho);
            Cargar(grid, e.Datos(grid));
            var (recortes, inspeccionadas) = Recortes(grid, window, proporcionales);
            Assert.True(inspeccionadas > 0, $"[{e.Nombre}] no se inspeccionó ninguna columna: el barrido no custodia nada.");
            fallas.AddRange(recortes.Select(r => $"[{e.Nombre} @{ventana}] {r}"));
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(fallas.Count == 0, "Recortes:\n" + string.Join("\n", fallas));
    }

    /// <summary>
    /// En la app real los renglones de la factura se agregan DE A UNO a una ObservableCollection
    /// (no llegan todos juntos): la primera fila es chica y las siguientes más anchas. Las columnas
    /// Auto tienen que seguir creciendo y el botón "Quitar" no puede quedar cortado en ningún paso.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1366)]
    [InlineData(1920)]
    public void IngresoPorFactura_RenglonesAgregadosDeAUno_NoRecortanColumnasNiElBotonQuitar(double ventana)
    {
        var (window, grid, proporcionales) = Montar(new IngresoPorFacturaView(), ventana - SidebarPx);
        var renglones = new ObservableCollection<FilaRenglonFacturaVm>();
        grid.ItemsSource = renglones;
        Dispatcher.UIThread.RunJobs();

        var fallas = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            renglones.Add(new FilaRenglonFacturaVm
            {
                Producto = Producto(i, 10), Cantidad = i == 0 ? 1m : 1.5m * (decimal)Math.Pow(10, i),
                PrecioUnitario = i == 0 ? 5.4m : 1234567.89m, ActualizarPrecioCosto = true,
            });
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            var (recortes, inspeccionadas) = Recortes(grid, window, proporcionales);
            Assert.True(inspeccionadas > 0, "no se inspeccionó ninguna columna: el test no custodia nada.");
            fallas.AddRange(recortes.Select(r => $"[tras {i + 1} renglón(es) @{ventana}] {r}"));
        }
        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(fallas.Count == 0, "Recortes:\n" + string.Join("\n", fallas.Distinct()));
    }
}
