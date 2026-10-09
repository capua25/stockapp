using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Historial de movimientos v2 (spec §5.1) con la View REAL y el tema real: estados de carga, sin permiso y vacío,
/// columnas medidas que no se mueven ni se cortan, stock negativo por token, gates de permisos en caliente y el
/// botón que abre el diálogo de Recalcular stock.
/// </summary>
public class MovimientoHistorialViewV2Tests
{
    private static readonly Color Etiqueta = Color.Parse("#44403C");
    private static readonly Color PeligroTexto = Color.Parse("#B91C1C");

    /// <summary>
    /// IMovimientoStockService con los tipos reales. PorFiltro decide por filtro (la carga inicial va sin filtros);
    /// si no está, Falla, Pendiente o Lista, en ese orden.
    /// </summary>
    private sealed class HistorialFake : IMovimientoStockService
    {
        public List<MovimientoHistorialDto> Lista { get; set; } = new();
        public TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>? Pendiente { get; set; }
        public Exception? Falla { get; set; }
        public Func<HistorialMovimientoFiltro, Task<IReadOnlyList<MovimientoHistorialDto>>>? PorFiltro { get; set; }

        public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro) =>
            PorFiltro is not null ? PorFiltro(filtro)
            : Falla is not null ? Task.FromException<IReadOnlyList<MovimientoHistorialDto>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<MovimientoHistorialDto>>(Lista.ToList());

        public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId) =>
            Task.FromResult(new RecalculoResultadoDto(productoId, 0m, 0m, 0));
    }

    private sealed class ProductosFake : IProductoService
    {
        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
    }

    private sealed class DialogoRecalcularFake : IRecalcularStockDialogService
    {
        public int Llamadas { get; private set; }

        public Task<ProductoDto?> ElegirProductoAsync(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar)
        {
            Llamadas++;
            return Task.FromResult<ProductoDto?>(null);
        }
    }

    /// <summary>Hora LOCAL fija convertida a UTC: la celda (UTC a local) muestra lo mismo en cualquier huso.</summary>
    private static DateTime Fecha(int mes, int dia, int hora, int minuto) =>
        new DateTime(2026, mes, dia, hora, minuto, 0, DateTimeKind.Local).ToUniversalTime();

    private const string Maria = "María Pérez";
    private const string Deposito = "Operador de Depósito Central";
    private const string ComentarioLargo =
        "Reparación del alumbrado de la plaza Independencia y del pasaje peatonal frente a la escuela 12.";

    private static MovimientoHistorialDto M(int id, string producto, TipoMovimiento tipo, MotivoMovimiento motivo, decimal cantidad,
        decimal precio, decimal anterior, decimal nuevo, string? comentario, DateTime fecha, string usuario = Maria) =>
        new(id, id, producto, tipo, motivo, cantidad, precio, anterior, nuevo, comentario, fecha, usuario == Maria ? 1 : 2, usuario);

    private static readonly MovimientoHistorialDto Compra =
        M(112, "Cemento portland 50 kg", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 40m, 480m, 280m, 320m, "Remito 0042-00001234", Fecha(10, 7, 14, 35));
    private static readonly MovimientoHistorialDto SalidaSinPrecio =
        M(111, "Cable 2x1,5 mm", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 25.5m, 0m, 1275.5m, 1250m, "Alumbrado de la plaza", Fecha(10, 6, 9, 10), Deposito);
    private static readonly MovimientoHistorialDto Negativo =
        M(108, "Hipoclorito de sodio 5 L", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 5m, 0m, 2m, -3m, "Limpieza del gimnasio municipal", Fecha(9, 30, 8, 20), Deposito);
    private static readonly MovimientoHistorialDto AnteriorNegativo =
        M(109, "Hipoclorito de sodio 5 L", TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, 5m, 0m, -3m, 2m, null, Fecha(10, 1, 16, 45));
    private static readonly MovimientoHistorialDto ConComentarioLargo =
        M(106, "Lámpara LED 30 W", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 4m, 0m, 12m, 8m, ComentarioLargo, Fecha(9, 24, 15, 5), Deposito);
    private static readonly MovimientoHistorialDto CompraCara =
        M(104, "Pintura vial amarilla 4 L", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 22.5m, 1890.5m, 0m, 22.5m, null, Fecha(9, 15, 9, 0));
    private static readonly MovimientoHistorialDto Merma =
        M(105, "Bolsas de residuo 80x110", TipoMovimiento.Salida, MotivoMovimiento.Merma, 10m, 0m, 70m, 60m, "Bolsas rotas en el depósito", Fecha(9, 19, 12, 30), Deposito);

    private static MovimientoHistorialDto[] Varios() => [Compra, SalidaSinPrecio, AnteriorNegativo, Negativo, ConComentarioLargo, Merma, CompraCara];

    private static readonly string[] Medidas = ["FECHA", "TIPO", "CANTIDAD", "STOCK ANT.", "MOTIVO", "PRECIO", "USUARIO"];

    private sealed record Montaje(Window Ventana, MovimientoHistorialView Vista, MovimientoHistorialViewModel Vm, HistorialFake Servicio,
        DialogoRecalcularFake Dialogo);

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    private static Montaje Montar(params MovimientoHistorialDto[] movimientos) => Montar(new HistorialFake { Lista = movimientos.ToList() });

    /// <summary>Monta la vista con el servicio ya configurado. 1040 es el ancho de la vista a 1280 con el sidebar de 240.</summary>
    private static Montaje Montar(HistorialFake servicio, double ancho = 1040, SesionFake? sesion = null)
    {
        var dialogo = new DialogoRecalcularFake();
        var vm = new MovimientoHistorialViewModel(servicio, new NavigationServiceFake(), new ProductosFake(), new ConfirmacionServiceFake(),
            sesion ?? Mixto(Permisos.RegistrarMovimientos, Permisos.RecalcularStock, Permisos.GestionarProductos), dialogo);
        var vista = new MovimientoHistorialView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, dialogo);
    }

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// La parte se busca en el árbol lógico (dentro de una card que nunca se mostró el template no se aplica y su
    /// contenido no llega al árbol visual) y, si no está ahí, en el visual (lo que va por una propiedad de plantilla,
    /// como las acciones del encabezado o los filtros de la barra). Visible es estar en el árbol visual sin ningún
    /// ancestro oculto.
    /// </summary>
    private static bool Visible(Montaje m, string nombre)
    {
        var parte = m.Vista.GetLogicalDescendants().Concat(m.Vista.GetVisualDescendants()).OfType<Control>().First(c => c.Name == nombre);
        return TopLevel.GetTopLevel(parte) is not null && ArbolVisual.EsVisibleEnArbol(parte);
    }

    private static DataGrid Grilla(Montaje m) => MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

    private static DataGridColumn Columna(DataGrid grilla, string header) => grilla.Columns.Single(c => Equals(c.Header, header));

    private static DataGridRow Fila(DataGrid grilla, object item) =>
        grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, item));

    /// <summary>
    /// El TextBlock de la celda de <paramref name="item"/> que está bajo el header de la columna. Antes la trae a la
    /// vista: con scroll horizontal la grilla no realiza las celdas de las columnas que quedan fuera (quedan ocultas).
    /// </summary>
    private static TextBlock CeldaDe(DataGrid grilla, object item, string header)
    {
        grilla.ScrollIntoView(item, Columna(grilla, header));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var h = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(x => Equals(x.Content, header));
        var centro = h.TranslatePoint(new Point(h.Bounds.Width / 2, 0), grilla)!.Value.X;
        var celda = Fila(grilla, item).GetVisualDescendants().OfType<DataGridCell>().Single(c =>
        {
            var x0 = c.TranslatePoint(default, grilla)!.Value.X;
            return c.IsVisible && x0 <= centro && centro <= x0 + c.Bounds.Width;
        });
        return celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.FindAncestorOfType<Insignia>() is null);
    }

    private static Color ColorDe(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    private static bool HayScrollHorizontal(DataGrid grilla) =>
        grilla.GetVisualDescendants().OfType<ScrollBar>().Any(s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);

    private static bool InsigniaVisible(DataGridRow fila) =>
        fila.GetVisualDescendants().OfType<Insignia>().Any(i => i.Texto == "Stock negativo" && ArbolVisual.EsVisibleEnArbol(i));

    [AvaloniaFact]
    public void Raiz_TieneUi2YUsaLosComponentesV2()
    {
        var m = Montar(Compra);

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Historial de movimientos", MontajeV2.Parte<EncabezadoPagina>(m.Vista, "Encabezado").Titulo);
        Assert.NotNull(MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta"));
        Assert.False(MontajeV2.Parte<BarraFiltros>(m.Vista, "Filtros").MostrarBusqueda);
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<StockApp.Presentation.Controls.BadgeEstado>());
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<StockApp.Presentation.Controls.HeaderVista>());
        Assert.Empty(m.Vista.GetLogicalDescendants().OfType<StockApp.Presentation.Controls.EstadoVacio>());
    }

    [AvaloniaFact]
    public void Encabezado_SoloRecalcularStockSecundario()
    {
        var m = Montar(Compra);
        var encabezado = MontajeV2.Parte<EncabezadoPagina>(m.Vista, "Encabezado");

        var boton = Assert.Single(encabezado.GetVisualDescendants().OfType<Button>(), ArbolVisual.EsVisibleEnArbol);
        Assert.Equal("Recalcular stock", boton.Content);
        Assert.Contains("secundario", boton.Classes);
        Assert.DoesNotContain(m.Vista.GetLogicalDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Columnas_LasDiezEnMayusculasYEnSuOrden()
    {
        var m = Montar(Compra);
        var headers = Grilla(m).Columns.Select(c => c.Header as string).ToArray();

        Assert.Equal(new string?[]
        {
            "FECHA", "PRODUCTO", "TIPO", "CANTIDAD", "STOCK ANT.", "STOCK NUEVO", "MOTIVO", "PRECIO", "USUARIO", "COMENTARIO",
        }, headers);
    }

    [AvaloniaFact]
    public void Cargando_SinFilas_MuestraElIndicadorYNiElVacioNiLaGrilla()
    {
        var m = Montar(new HistorialFake { Pendiente = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>() });

        Assert.True(m.Vm.Cargando);
        Assert.True(Visible(m, "IndicadorCarga"));
        Assert.Equal("Cargando movimientos…", MontajeV2.Parte<EstadoCargando>(m.Vista, "IndicadorCarga").Texto);
        Assert.False(Visible(m, "EstadoSinMovimientos"));
        Assert.False(Visible(m, "Grilla"));
        Assert.False(Visible(m, "PieConteo"));
        Assert.False(Visible(m, "EstadoSinPermiso"));
    }

    [AvaloniaFact]
    public void AlTerminarLaCargaSinMovimientos_ElIndicadorDaPasoAlEstadoVacio()
    {
        var pendiente = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();
        var m = Montar(new HistorialFake { Pendiente = pendiente });

        pendiente.SetResult([]);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(m, "IndicadorCarga"));
        Assert.True(Visible(m, "EstadoSinMovimientos"));
        var vacio = MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinMovimientos");
        Assert.Equal("No hay movimientos", vacio.Titulo);
        Assert.Equal("Probá con otros filtros o con otras fechas.", vacio.Mensaje);
        Assert.False(Visible(m, "Grilla"));
    }

    [AvaloniaFact]
    public void Buscar_ConFilasEnPantalla_NoMuestraElIndicadorYLaGrillaSigue()
    {
        var m = Montar(Compra, Negativo);
        m.Servicio.Pendiente = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.True(m.Vm.Cargando);
        Assert.False(Visible(m, "IndicadorCarga"));
        Assert.True(Visible(m, "Grilla"));
    }

    [AvaloniaFact]
    public void SinPermiso_EnLaCarga_MuestraSinPermisoEnLugarDeLaCardYNuncaElVacio()
    {
        var m = Montar(new HistorialFake { Falla = new UnauthorizedAccessException("403") });

        Assert.True(Visible(m, "EstadoSinPermiso"));
        var estado = MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinPermiso");
        Assert.True(estado.EsError);
        Assert.Equal("Sin permiso", estado.Titulo);
        Assert.Equal(MovimientoHistorialViewModel.MensajeSinPermisoHistorial, estado.Mensaje);
        Assert.True(Visible(m, "Encabezado"));
        Assert.False(Visible(m, "Tarjeta"));
        Assert.False(Visible(m, "EstadoSinMovimientos"));
        Assert.False(Visible(m, "IndicadorCarga"));
    }

    [AvaloniaFact]
    public void SinPermiso_EnUnaBusqueda_ReemplazaLaGrillaPorSinPermiso()
    {
        var m = Montar(Compra);
        m.Servicio.Falla = new UnauthorizedAccessException("403");

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.True(Visible(m, "EstadoSinPermiso"));
        Assert.False(Visible(m, "Tarjeta"));
    }

    [AvaloniaFact]
    public void BuscarConLaCargaInicialEnCurso_LaGrillaQuedaConLaBusqueda()
    {
        // La carga inicial (sin filtros) queda colgada; el usuario elige Salida y busca; la búsqueda vuelve primero y
        // la carga, al llegar tarde, no la pisa.
        var cargaInicial = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();
        var servicio = new HistorialFake
        {
            PorFiltro = f => f.Tipo is null
                ? cargaInicial.Task
                : Task.FromResult<IReadOnlyList<MovimientoHistorialDto>>([SalidaSinPrecio]),
        };
        var m = Montar(servicio);

        var combo = m.Vista.GetVisualDescendants().OfType<ComboBox>().Single();
        combo.SelectedItem = m.Vm.TiposDisponibles.Single(t => t.Valor == TipoMovimiento.Salida);
        Dispatcher.UIThread.RunJobs();
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        cargaInicial.SetResult([Compra, SalidaSinPrecio, Negativo]);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var grilla = Grilla(m);
        var filas = grilla.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible).Select(r => r.DataContext).ToList();
        Assert.Equal(new object[] { SalidaSinPrecio }, filas);
        Assert.Equal("1 movimiento", MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void PieDeConteo_SingularYPluralConMilesEsUy()
    {
        var m = Montar(Compra);
        Assert.Equal("1 movimiento", MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo").Text);

        m.Servicio.Lista = Enumerable.Range(1, 1500).Select(i => Compra with { MovimientoId = i }).ToList();
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));
        Assert.Equal("1.500 movimientos", MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void StockNegativo_InsigniaPeligroYNumeroEnPeligroTexto()
    {
        var m = Montar(Compra, Negativo, AnteriorNegativo);
        var grilla = Grilla(m);

        // STOCK NUEVO negativo: insignia peligro y el número en rojo.
        var negativa = Fila(grilla, Negativo);
        var insignia = negativa.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Stock negativo");
        Assert.True(ArbolVisual.EsVisibleEnArbol(insignia));
        Assert.Equal(VarianteInsignia.Peligro, insignia.Variante);
        Assert.Equal(PeligroTexto, ColorDe(CeldaDe(grilla, Negativo, "STOCK NUEVO")));
        Assert.Equal("-3", CeldaDe(grilla, Negativo, "STOCK NUEVO").Text);

        // STOCK ANT. negativo: solo el número en rojo; la fila no lleva insignia (su STOCK NUEVO es positivo).
        Assert.Equal(PeligroTexto, ColorDe(CeldaDe(grilla, AnteriorNegativo, "STOCK ANT.")));
        Assert.Equal(Etiqueta, ColorDe(CeldaDe(grilla, AnteriorNegativo, "STOCK NUEVO")));
        Assert.False(InsigniaVisible(Fila(grilla, AnteriorNegativo)));

        // Fila positiva: sin insignia y los números en el color de siempre.
        Assert.False(InsigniaVisible(Fila(grilla, Compra)));
        Assert.Equal(Etiqueta, ColorDe(CeldaDe(grilla, Compra, "STOCK NUEVO")));
        Assert.Equal(Etiqueta, ColorDe(CeldaDe(grilla, Compra, "STOCK ANT.")));
    }

    [AvaloniaFact]
    public void Precio_CeroSeMuestraComoGuion()
    {
        var m = Montar(Compra, SalidaSinPrecio);
        var grilla = Grilla(m);

        Assert.Equal("—", CeldaDe(grilla, SalidaSinPrecio, "PRECIO").Text);
        Assert.Equal("$ 480,00", CeldaDe(grilla, Compra, "PRECIO").Text);
    }

    [AvaloniaFact]
    public void Fecha_HoraLocalConFormatoFijo()
    {
        var m = Montar(Compra with { Fecha = Fecha(10, 7, 14, 35) });
        var grilla = Grilla(m);

        Assert.Equal("07/10/2026 14:35", CeldaDe(grilla, m.Vm.Items.Single(), "FECHA").Text);
    }

    [AvaloniaFact]
    public void Scroll_MasDeMilMovimientos_HastaElFinal_NoMueveNingunaColumna()
    {
        // 1200 movimientos; la fecha, la cantidad, el precio, el stock y el usuario más anchos están en la ÚLTIMA fila,
        // fuera de pantalla. Las columnas medidas nacen con el ancho de todas las filas y no saltan al llegar a ella.
        // Las demás filas llevan la fecha más angosta (los dígitos de Inter no son tabulares: "11/11/2026 11:11" mide
        // varios px menos que "28/12/2026 20:48").
        var movimientos = Enumerable.Range(1, 1199)
            .Select(i => M(i, $"Producto {i}", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 1m, 10m, 1m, 2m, null, Fecha(11, 11, 11, 11)))
            .ToList();
        movimientos.Add(M(1200, "Último", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 99888.5m, 98765432.10m, 99999.5m,
            1.5m, null, Fecha(12, 28, 20, 48), "Operadora del Depósito Central"));
        var m = Montar(movimientos.ToArray());
        var grilla = Grilla(m);
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();

        grilla.ScrollIntoView(movimientos[^1], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, movimientos[^1]));
        var despues = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        for (var i = 0; i < antes.Length; i++)
            Assert.True(Math.Abs(antes[i] - despues[i]) <= 0.5,
                $"La columna {grilla.Columns[i].Header} pasó de {antes[i]} a {despues[i]} al llegar a la fila 1200.");
        foreach (var header in new[] { "FECHA", "CANTIDAD", "PRECIO", "STOCK ANT.", "USUARIO" })
            Assert.False(TooltipSiSeCorta.EstaCortado(CeldaDe(grilla, movimientos[^1], header)), $"{header} de la fila 1200 quedó cortado.");
    }

    [AvaloniaFact]
    public void Ordenar_PorCadaColumna_NoMueveAnchosNiCortaElTitulo()
    {
        // A 1700 las diez columnas entran sin scroll y cada header se puede clickear (a 1040 los de la derecha están
        // fuera de la vista). Lo que se custodia es lo mismo: ordenar no cambia ningún ancho ni corta un título.
        var m = Montar(new HistorialFake { Lista = Varios().ToList() }, ancho: 1700);
        var grilla = Grilla(m);
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();

        var headers = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string).ToList();
        Assert.Equal(10, headers.Count);
        foreach (var header in headers)
        {
            Clickear(m.Ventana, header);
            var titulo = header.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
            Assert.True(titulo.Bounds.Width >= GrillasSinRecorteBarridoTests.AnchoNatural(titulo) - 0.5, $"{titulo.Text} ordenado quedó cortado.");
            Assert.Equal(antes, grilla.Columns.Select(c => c.ActualWidth).ToArray());
        }
    }

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(1126)]
    public void A1280y1366_LasMedidasSeLeenEnterasYLaGrillaScrollea(double ancho)
    {
        // La vista a 1280 y a 1366 sin el sidebar de 240: las diez columnas no entran ni cediendo, así que las medidas
        // quedan en su ancho medido (texto entero), PRODUCTO y COMENTARIO en su piso y la grilla scrollea.
        var movimientos = Varios();
        var m = Montar(new HistorialFake { Lista = movimientos.ToList() }, ancho);
        var grilla = Grilla(m);

        // Antes de scrollear: STOCK NUEVO (y la insignia) entra entera en la parte visible de la grilla.
        var stockNuevo = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "STOCK NUEVO"));
        var bordeDerecho = stockNuevo.TranslatePoint(new Point(stockNuevo.Bounds.Width, 0), grilla)!.Value.X;
        Assert.True(bordeDerecho <= grilla.Bounds.Width - 1, $"STOCK NUEVO termina en x={bordeDerecho} y la grilla se ve hasta {grilla.Bounds.Width - 1}.");

        foreach (var mov in movimientos)
            foreach (var header in Medidas)
                Assert.False(TooltipSiSeCorta.EstaCortado(CeldaDe(grilla, mov, header)), $"{header} de {mov.ProductoNombre} quedó cortado.");
        Assert.Equal(160, Columna(grilla, "PRODUCTO").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "COMENTARIO").ActualWidth, 1);
        Assert.True(HayScrollHorizontal(grilla));
    }

    [AvaloniaFact]
    public void CediendoEntra_FechaQuedaEnSuAnchoMedido_YCedeOtraMedida()
    {
        // 1350 de vista: con las medidas en su ancho no entra, pero cediendo sí (sin scroll). FECHA es de formato
        // cerrado: nunca cede (un "07/10/20…" no se lee); el lugar sale de las demás medidas.
        var movimientos = Varios();
        var m = Montar(new HistorialFake { Lista = movimientos.ToList() }, ancho: 1350);
        var grilla = Grilla(m);
        var fecha = Columna(grilla, "FECHA");

        Assert.False(HayScrollHorizontal(grilla), "Precondición: a 1350 cediendo entra.");
        Assert.Equal(DataGridColumnasAutoBehavior.AnchoMedido(grilla, fecha)!.Value, fecha.ActualWidth, 1);
        foreach (var mov in movimientos)
            Assert.False(TooltipSiSeCorta.EstaCortado(CeldaDe(grilla, mov, "FECHA")), $"FECHA de {mov.ProductoNombre} quedó cortada.");
        var cedidas = Medidas.Where(h => h != "FECHA")
            .Where(h => Columna(grilla, h).ActualWidth < DataGridColumnasAutoBehavior.AnchoMedido(grilla, Columna(grilla, h))!.Value - 0.5)
            .ToList();
        Assert.NotEmpty(cedidas);
    }

    [AvaloniaFact]
    public void Paridad_ElTextoMedidoEsElTextoPintado()
    {
        var movimientos = Varios();
        var m = Montar(movimientos);
        var grilla = Grilla(m);

        foreach (var mov in movimientos)
            foreach (var header in Medidas)
                Assert.Equal(CeldaDe(grilla, mov, header).Text, ColumnaAuto.Texto(Columna(grilla, header), mov));
    }

    [AvaloniaFact]
    public void ComentarioLargo_SeCortaYElTooltipAbre_YElQueEntraNo()
    {
        // COMENTARIO queda en su piso de 160: el largo se corta y uno corto entra entero.
        var conCorto = Compra with { Comentario = "Remito 42" };
        var m = Montar(conCorto, ConComentarioLargo);
        var grilla = Grilla(m);

        var largo = CeldaDe(grilla, ConComentarioLargo, "COMENTARIO");
        Assert.True(TooltipSiSeCorta.EstaCortado(largo));
        Assert.False(TooltipSiSeCortaTests.SeCancela(largo));
        Assert.Equal(ComentarioLargo, ToolTip.GetTip(largo));

        var corto = CeldaDe(grilla, conCorto, "COMENTARIO");
        Assert.False(TooltipSiSeCorta.EstaCortado(corto));
        Assert.True(TooltipSiSeCortaTests.SeCancela(corto));
    }

    [AvaloniaFact]
    public void SinGestionarProductos_ElFiltroDeProductoNoEstaYTipoArrancaALaIzquierda()
    {
        var m = Montar(new HistorialFake { Lista = { Compra } }, sesion: Mixto(Permisos.RegistrarMovimientos));
        var filtros = MontajeV2.Parte<BarraFiltros>(m.Vista, "Filtros");

        Assert.False(Visible(m, "CampoProducto"));
        var tipo = filtros.GetVisualDescendants().OfType<CampoFormulario>().Single(c => c.Etiqueta == "Tipo");
        Assert.Equal(0, tipo.TranslatePoint(default, filtros)!.Value.X, 0.5);
    }

    [AvaloniaFact]
    public void RevocarRecalcularStock_EnCaliente_OcultaElBotonSinRecrearLaVista()
    {
        var sesion = Mixto(Permisos.RegistrarMovimientos, Permisos.RecalcularStock);
        var m = Montar(new HistorialFake { Lista = { Compra } }, sesion: sesion);
        Assert.True(Visible(m, "BotonRecalcular"), "Precondición: con el permiso, Recalcular stock se ve.");

        sesion.EstablecerPermisos(new HashSet<string> { Permisos.RegistrarMovimientos });
        m.Vm.NotificarPermisosActualizados();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(m, "BotonRecalcular"));
    }

    [AvaloniaFact]
    public async Task Un403_ConElHistorialAbierto_OcultaRecalcularDesdeElShell()
    {
        var sesion = Mixto(Permisos.RegistrarMovimientos, Permisos.RecalcularStock);
        var tras = new HashSet<string> { Permisos.RegistrarMovimientos };
        var shellVm = new ShellMainViewModel(
            sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var historial = new MovimientoHistorialViewModel(new HistorialFake { Lista = { Compra } }, new NavigationServiceFake(),
            new ProductosFake(), new ConfirmacionServiceFake(), sesion, new DialogoRecalcularFake());
        shellVm.CurrentContent = historial;

        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shellVm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Control Recalcular() => ventana.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BotonRecalcular");
        Assert.True(ArbolVisual.EsVisibleEnArbol(Recalcular()), "Precondición: con el permiso, Recalcular stock se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras),
            new ConfirmacionServiceFake(), shellVm.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(Recalcular()));
    }

    [AvaloniaFact]
    public void Recalcular_ClickReal_AbreElDialogoDelServicio()
    {
        var m = Montar(Compra);

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonRecalcular"));

        Assert.Equal(1, m.Dialogo.Llamadas);
    }
}
