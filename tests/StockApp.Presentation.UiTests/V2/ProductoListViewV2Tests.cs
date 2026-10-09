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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using Avalonia.Markup.Xaml;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Listado de Productos v2 (spec §5.1) con la View REAL y el tema real: lo que se mide es el efecto (color
/// pintado, opacidad, habilitación, navegación por click real), no la configuración del XAML.
/// </summary>
public class ProductoListViewV2Tests
{
    private static readonly Color Terciario = Color.Parse("#78716C");
    private static readonly Color Etiqueta = Color.Parse("#44403C");
    private static readonly Color PeligroTexto = Color.Parse("#B91C1C");

    private sealed class ProductosFake : IProductoService
    {
        public List<ProductoDto> Lista { get; set; } = new();
        public List<int> Bajas { get; } = new();
        public TaskCompletionSource<IReadOnlyList<ProductoDto>>? Pendiente { get; set; }
        public Exception? Falla { get; set; }

        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();

        public Task BajaLogicaAsync(int id)
        {
            Bajas.Add(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) =>
            Responder(Lista.ToList());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) =>
            Responder(Lista.Where(p => string.IsNullOrEmpty(texto) || p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());

        private Task<IReadOnlyList<ProductoDto>> Responder(List<ProductoDto> resultado) =>
            Falla is not null ? Task.FromException<IReadOnlyList<ProductoDto>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<ProductoDto>>(resultado);
    }

    private static ProductoDto P(int id, string nombre, decimal stock, bool activo = true) => new(
        id, $"C-{id:000}", null, nombre, null, 1, "Construcción", null, 1, "Unidad", 100m + id, stock, 0m, activo,
        new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static readonly ProductoDto Activo = P(1, "Cemento portland 50 kg", 320m);
    private static readonly ProductoDto Negativo = P(2, "Hipoclorito de sodio 5 L", -3m);
    private static readonly ProductoDto Inactivo = P(3, "Carretilla reforzada", 4m, activo: false);
    private static readonly ProductoDto InactivoNegativo = P(4, "Pala de punta", -7m, activo: false);

    private sealed record Montaje(Window Ventana, ProductoListView Vista, ProductoListViewModel Vm, ProductosFake Servicio,
        NavigationRecorderFake Navegacion);

    private static Montaje Montar(params ProductoDto[] productos) => Montar(new ProductosFake { Lista = productos.ToList() });

    /// <summary>Monta la vista con el servicio ya configurado. 1040 es el ancho de la vista a 1280 con el sidebar de 240.</summary>
    private static Montaje Montar(ProductosFake servicio, double ancho = 1040, SesionFake? sesion = null)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = new ProductoListViewModel(servicio, navegacion, new ConfirmacionServiceFake(), sesion ?? new SesionFake(RolUsuario.Operador, Permisos.GestionarProductos));
        var vista = new ProductoListView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
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

    private static DataGridRow Fila(Montaje m, ProductoDto producto) =>
        m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, producto));

    /// <summary>TextBlocks de las celdas de la fila que NO son de una Insignia (la insignia pinta su propio color).</summary>
    private static List<TextBlock> TextosDeCeldas(DataGridRow fila) =>
        fila.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null)
            .ToList();

    private static Color ColorDe(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    /// <summary>
    /// La parte se busca en el árbol lógico: dentro de una card que nunca se mostró el template no se aplica y su
    /// contenido no llega al árbol visual. Visible es estar en el árbol visual sin ningún ancestro oculto.
    /// </summary>
    private static bool Visible(Montaje m, string nombre)
    {
        var parte = m.Vista.GetLogicalDescendants().OfType<Control>().First(c => c.Name == nombre);
        return TopLevel.GetTopLevel(parte) is not null && ArbolVisual.EsVisibleEnArbol(parte);
    }

    private static DataGridColumn Columna(DataGrid grilla, string header) => grilla.Columns.Single(c => Equals(c.Header, header));

    /// <summary>El TextBlock de la celda de <paramref name="item"/> que está bajo el header de la columna.</summary>
    private static TextBlock CeldaDe(DataGrid grilla, object item, string header)
    {
        var h = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(x => Equals(x.Content, header));
        var centro = h.TranslatePoint(new Point(h.Bounds.Width / 2, 0), grilla)!.Value.X;
        var fila = grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, item));
        var celda = fila.GetVisualDescendants().OfType<DataGridCell>().Single(c =>
        {
            var x0 = c.TranslatePoint(default, grilla)!.Value.X;
            return x0 <= centro && centro <= x0 + c.Bounds.Width;
        });
        return celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.FindAncestorOfType<Insignia>() is null);
    }

    [AvaloniaFact]
    public void Raiz_TieneUi2YUsaLosComponentesV2()
    {
        var m = Montar(Activo);

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Productos", MontajeV2.Parte<EncabezadoPagina>(m.Vista, "Encabezado").Titulo);
        Assert.NotNull(MontajeV2.Parte<BarraFiltros>(m.Vista, "Filtros"));
        Assert.NotNull(MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta"));
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<StockApp.Presentation.Controls.HeaderVista>());
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<StockApp.Presentation.Controls.BadgeEstado>());
    }

    [AvaloniaFact]
    public void UnSoloPrimario_YEsNuevo()
    {
        var m = Montar(Activo);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("primario") && ArbolVisual.EsVisibleEnArbol(b)).ToList();

        var nuevo = Assert.Single(primarios);
        Assert.Equal("Nuevo", nuevo.Content);
        Assert.Same(m.Vm.NuevoCommand, nuevo.Command);
    }

    [AvaloniaFact]
    public void Columnas_LasSieteActualesEnMayusculasYEnSuOrden()
    {
        var m = Montar(Activo);
        var headers = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla").Columns.Select(c => c.Header as string).ToArray();

        Assert.Equal(new string?[] { "CÓDIGO", "NOMBRE", "COSTO", "STOCK", "UNIDAD", "CATEGORÍA", "ESTADO" }, headers);
    }

    [AvaloniaFact]
    public void ProductoInactivo_TextoTerciarioInsigniaPlenaYSinOpacidad()
    {
        var m = Montar(Activo, Inactivo);
        var fila = Fila(m, Inactivo);

        var textos = TextosDeCeldas(fila);
        Assert.Equal(6, textos.Count); // código, nombre, costo, stock, unidad, categoría
        Assert.All(textos, t => Assert.Equal(Terciario, ColorDe(t)));
        Assert.All(TextosDeCeldas(Fila(m, Activo)), t => Assert.Equal(Etiqueta, ColorDe(t)));

        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Inactivo");
        Assert.True(ArbolVisual.EsVisibleEnArbol(insignia));
        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);

        // Ninguna opacidad entre cada texto (o la insignia) y la grilla: el inactivo se marca por color, nunca por Opacity.
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        foreach (var v in textos.Cast<Visual>().Append(insignia))
        {
            for (Visual? a = v; a is not null && !ReferenceEquals(a, grilla); a = a.GetVisualParent())
                Assert.True(a.Opacity == 1, $"{a.GetType().Name} con Opacity {a.Opacity} en la fila inactiva");
        }
    }

    [AvaloniaFact]
    public void StockNegativo_InsigniaPeligroYNumeroEnPeligroTexto()
    {
        var m = Montar(Activo, Negativo);

        var negativa = Fila(m, Negativo);
        var insignia = negativa.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Stock negativo");
        Assert.True(ArbolVisual.EsVisibleEnArbol(insignia));
        Assert.Equal(VarianteInsignia.Peligro, insignia.Variante);
        Assert.Equal(PeligroTexto, ColorDe(TextosDeCeldas(negativa).Single(t => t.Text == "-3")));

        var positiva = Fila(m, Activo);
        Assert.DoesNotContain(positiva.GetVisualDescendants().OfType<Insignia>(),
            i => i.Texto == "Stock negativo" && ArbolVisual.EsVisibleEnArbol(i));
        Assert.Equal(Etiqueta, ColorDe(TextosDeCeldas(positiva).Single(t => t.Text == "320")));
    }

    [AvaloniaFact]
    public void InactivoConStockNegativo_ElNumeroSigueEnRojo()
    {
        var m = Montar(InactivoNegativo);
        var textos = TextosDeCeldas(Fila(m, InactivoNegativo));

        Assert.Equal(PeligroTexto, ColorDe(textos.Single(t => t.Text == "-7")));
        Assert.All(textos.Where(t => t.Text != "-7"), t => Assert.Equal(Terciario, ColorDe(t)));
    }

    [AvaloniaFact]
    public void EditarYEliminar_SinSeleccion_Deshabilitados()
    {
        var m = Montar(Activo, Inactivo);

        Assert.False(MontajeV2.Parte<Button>(m.Vista, "BotonEditar").IsEffectivelyEnabled);
        Assert.False(MontajeV2.Parte<Button>(m.Vista, "BotonEliminar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void EditarYEliminar_ConActivoSeleccionado_Habilitados()
    {
        var m = Montar(Activo, Inactivo);
        Clickear(m.Ventana, Fila(m, Activo));

        Assert.Same(Activo, m.Vm.ItemSeleccionado);
        Assert.True(MontajeV2.Parte<Button>(m.Vista, "BotonEditar").IsEffectivelyEnabled);
        Assert.True(MontajeV2.Parte<Button>(m.Vista, "BotonEliminar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void EditarYEliminar_ConInactivoSeleccionado_Deshabilitados()
    {
        var m = Montar(Activo, Inactivo);
        Clickear(m.Ventana, Fila(m, Inactivo));

        Assert.Same(Inactivo, m.Vm.ItemSeleccionado);
        Assert.False(MontajeV2.Parte<Button>(m.Vista, "BotonEditar").IsEffectivelyEnabled);
        Assert.False(MontajeV2.Parte<Button>(m.Vista, "BotonEliminar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Nuevo_ClickReal_NavegaAlFormulario()
    {
        var m = Montar(Activo);
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonNuevo"));

        // NuevoAsync navega dentro de Task.Run.
        Assert.True(SpinWait.SpinUntil(() => m.Navegacion.UltimoTipoNavegado is not null, TimeSpan.FromSeconds(5)));
        Assert.Equal(typeof(ProductoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Editar_ClickReal_AbreElFormularioConElSeleccionado()
    {
        var m = Montar(Activo, Negativo);
        Clickear(m.Ventana, Fila(m, Negativo));
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonEditar"));

        Assert.True(SpinWait.SpinUntil(() => m.Navegacion.UltimoInicializador is not null, TimeSpan.FromSeconds(5)));
        var inicializar = Assert.IsType<Action<ProductoFormViewModel>>(m.Navegacion.UltimoInicializador);
        var formulario = new ProductoFormViewModel(new ProductoServiceFake(),
            new UnidadMedidaServiceFake(new List<UnidadMedida>()), new CategoriaServiceFake(new List<Categoria>()),
            new NavigationRecorderFake());
        inicializar(formulario);
        Assert.Equal(Negativo.Codigo, formulario.Codigo);
        Assert.True(formulario.EsEdicion);
    }

    [AvaloniaFact]
    public void Eliminar_ClickReal_ConfirmaYDaDeBajaElSeleccionado()
    {
        var m = Montar(Activo, Negativo);
        Clickear(m.Ventana, Fila(m, Negativo));
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonEliminar"));

        Assert.Equal(new[] { Negativo.Id }, m.Servicio.Bajas);
    }

    [AvaloniaFact]
    public void ListaVacia_MuestraEstadoVacio_YAlLlegarDatosVuelveLaGrilla()
    {
        var m = Montar();

        Assert.True(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinProductos")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<DataGrid>(m.Vista, "Grilla")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo")));
        Assert.False(Visible(m, "IndicadorCarga"));

        m.Servicio.Lista = [Activo, Negativo];
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinProductos")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<DataGrid>(m.Vista, "Grilla")));
        Assert.Equal("2 productos", MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void PieDeConteo_SingularYPluralConMilesEsUy()
    {
        var m = Montar(Activo);
        Assert.Equal("1 producto", MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo").Text);

        m.Servicio.Lista = Enumerable.Range(1, 1250).Select(i => P(i, $"Producto {i}", 1m)).ToList();
        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));
        Assert.Equal("1.250 productos", MontajeV2.Parte<TextBlock>(m.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void Busqueda_TipeadaEnLaBarra_LlegaAlFiltroDelVm()
    {
        var m = Montar(Activo);
        var caja = MontajeV2.Parte<BarraFiltros>(m.Vista, "Filtros").GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_Busqueda");

        caja.Text = "cemento";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("cemento", m.Vm.FiltroBusqueda);
        Assert.Equal("Nombre, SKU o código de barras", caja.PlaceholderText);
    }

    [AvaloniaFact]
    public void Scroll_HastaLaPrimeraFilaInactiva_NoMueveNingunaColumna()
    {
        // 30 productos activos con textos cortos y, al final (fuera de pantalla al inicio), uno inactivo y uno con
        // stock negativo: la grilla mide solo las filas realizadas y una Auto crece cuando aparece una fila más ancha.
        var productos = Enumerable.Range(1, 30).Select(i => P(i, $"Producto {i}", i)).ToList();
        productos.Add(P(31, "Carretilla reforzada", 4m, activo: false));
        productos.Add(P(32, "Pala de punta", -7m));
        var m = Montar(productos.ToArray());
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        Assert.DoesNotContain(grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, productos[30]));

        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        grilla.ScrollIntoView(productos[31], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, productos[30]));
        var despues = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        for (var i = 0; i < antes.Length; i++)
            Assert.True(Math.Abs(antes[i] - despues[i]) <= 0.5,
                $"La columna {grilla.Columns[i].Header} pasó de {antes[i]} a {despues[i]} al aparecer la fila inactiva.");
    }

    [AvaloniaFact]
    public void Cargando_SinFilas_MuestraElIndicadorYNiElVacioNiLaGrilla()
    {
        var m = Montar(new ProductosFake { Pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>() });

        Assert.True(m.Vm.Cargando);
        Assert.True(Visible(m, "IndicadorCarga"));
        Assert.Equal("Cargando productos…", MontajeV2.Parte<EstadoCargando>(m.Vista, "IndicadorCarga").Texto);
        Assert.False(Visible(m, "EstadoSinProductos"));
        Assert.False(Visible(m, "Grilla"));
        Assert.False(Visible(m, "PieConteo"));
        Assert.False(Visible(m, "EstadoSinPermiso"));
    }

    [AvaloniaFact]
    public void AlTerminarLaCargaSinProductos_ElIndicadorDaPasoAlEstadoVacio()
    {
        var pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var m = Montar(new ProductosFake { Pendiente = pendiente });

        pendiente.SetResult([]);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(m, "IndicadorCarga"));
        Assert.True(Visible(m, "EstadoSinProductos"));
    }

    [AvaloniaFact]
    public void Recarga_ConFilasEnPantalla_NoMuestraElIndicadorYLaGrillaSigue()
    {
        var m = Montar(Activo, Negativo);
        m.Servicio.Pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.True(m.Vm.Cargando);
        Assert.False(Visible(m, "IndicadorCarga"));
        Assert.True(Visible(m, "Grilla"));
    }

    [AvaloniaFact]
    public void SinPermiso_EnLaCarga_MuestraSinPermisoEnLugarDeLaCardYNuncaElVacio()
    {
        var m = Montar(new ProductosFake { Falla = new UnauthorizedAccessException("403") });

        Assert.True(Visible(m, "EstadoSinPermiso"));
        var estado = MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinPermiso");
        Assert.True(estado.EsError);
        Assert.Equal("Sin permiso", estado.Titulo);
        Assert.Equal(ProductoListViewModel.MensajeSinPermisoListado, estado.Mensaje);
        Assert.False(Visible(m, "Tarjeta"));
        Assert.False(Visible(m, "EstadoSinProductos"));
        Assert.False(Visible(m, "IndicadorCarga"));
    }

    [AvaloniaFact]
    public void SinPermiso_EnUnaBusqueda_ReemplazaLaGrillaPorSinPermiso()
    {
        var m = Montar(Activo);
        m.Servicio.Falla = new UnauthorizedAccessException("403");

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.True(Visible(m, "EstadoSinPermiso"));
        Assert.False(Visible(m, "Tarjeta"));
    }

    [AvaloniaFact]
    public void Scroll_MasDeMilFilas_HastaElFinal_NoMueveNingunaColumna()
    {
        // 1200 productos; los valores más anchos de CÓDIGO, COSTO y UNIDAD están en la ÚLTIMA fila, fuera de pantalla.
        // Los tres entran juntos en el espacio libre de la grilla a 1040 (104 + 123 + 97 de 332): ninguna columna
        // cede, así que ninguno puede quedar cortado. Un código más largo haría ceder a CÓDIGO con elipsis (el tope
        // se adapta al espacio libre) y ese corte sería el admitido, no un error de medición.
        var productos = Enumerable.Range(1, 1199).Select(i => P(i, $"Producto {i}", i)).ToList();
        productos.Add(new ProductoDto(1200, "MAT-0001234", null, "Último", null, 1, "Construcción", null, 1,
            "Metro cúbico", 98765432.10m, 3m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)));
        var m = Montar(productos.ToArray());
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();

        grilla.ScrollIntoView(productos[^1], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, productos[^1]));
        var despues = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        for (var i = 0; i < antes.Length; i++)
            Assert.True(Math.Abs(antes[i] - despues[i]) <= 0.5,
                $"La columna {grilla.Columns[i].Header} pasó de {antes[i]} a {despues[i]} al llegar a la fila 1200.");
        foreach (var header in new[] { "CÓDIGO", "COSTO", "UNIDAD" })
            Assert.False(TooltipSiSeCorta.EstaCortado(CeldaDe(grilla, productos[^1], header)), $"{header} quedó cortado sin pasar el tope.");
    }

    [AvaloniaFact]
    public void Scroll_MasDeMilFilas_ConUnCodigoQueNoEntra_NoSaltaYElCorteEsElAdmitido()
    {
        // Núcleo de U2: el código más largo está en la ÚLTIMA fila, fuera de pantalla, y no entra junto con COSTO y
        // UNIDAD en el espacio libre (1040): CÓDIGO cede. La columna ya nació con ese ancho (medida sobre todas las
        // filas) y no se mueve al llegar a la fila; ahí el corte es el admitido: elipsis y tooltip con el código entero.
        var productos = Enumerable.Range(1, 1199).Select(i => P(i, $"Producto {i}", i)).ToList();
        productos.Add(new ProductoDto(1200, "MAT-CONSTRUCCION-0001", null, "Último", null, 1, "Construcción", null, 1,
            "Metro cúbico", 98765432.10m, 3m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)));
        var m = Montar(productos.ToArray());
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        // La fila larga no está realizada y CÓDIGO ya cedió al espacio libre (116); medida solo sobre lo realizado
        // ("C-001") quedaría en su mínimo y saltaría al llegar a la fila.
        Assert.Equal(116, Columna(grilla, "CÓDIGO").ActualWidth, 1);

        grilla.ScrollIntoView(productos[^1], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, productos[^1]));
        var despues = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        for (var i = 0; i < antes.Length; i++)
            Assert.True(Math.Abs(antes[i] - despues[i]) <= 0.5,
                $"La columna {grilla.Columns[i].Header} pasó de {antes[i]} a {despues[i]} al llegar a la fila 1200.");
        var codigo = CeldaDe(grilla, productos[^1], "CÓDIGO");
        Assert.True(TooltipSiSeCorta.EstaCortado(codigo));
        Assert.False(TooltipSiSeCortaTests.SeCancela(codigo));
        Assert.Equal("MAT-CONSTRUCCION-0001", ToolTip.GetTip(codigo));
    }

    [AvaloniaFact]
    public void Ordenar_PorCadaColumna_NoMueveAnchosNiCortaElTitulo()
    {
        var m = Montar(Activo, Negativo, Inactivo, InactivoNegativo);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();

        foreach (var header in grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string s && s != "ESTADO").ToList())
        {
            Clickear(m.Ventana, header);
            var titulo = header.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
            Assert.True(titulo.Bounds.Width >= GrillasSinRecorteBarridoTests.AnchoNatural(titulo) - 0.5, $"{titulo.Text} ordenado quedó cortado.");
            Assert.Equal(antes, grilla.Columns.Select(c => c.ActualWidth).ToArray());
        }
    }

    [AvaloniaFact]
    public void Paridad_ElTextoMedidoEsElTextoPintado()
    {
        var m = Montar(Activo, Negativo, Inactivo, InactivoNegativo);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        foreach (var p in new[] { Activo, Negativo, Inactivo, InactivoNegativo })
            foreach (var header in new[] { "CÓDIGO", "COSTO", "UNIDAD" })
                Assert.Equal(CeldaDe(grilla, p, header).Text, ColumnaAuto.Texto(Columna(grilla, header), p));
    }

    [AvaloniaFact]
    public void CeldaCortada_MuestraElipsisYTooltip_YLasQueEntranNo()
    {
        var largo = P(5, "Arena gruesa", 45m) with { UnidadMedidaNombre = "Metro cúbico puesto en obra con flete incluido" };
        var corto = P(6, "Pala", 10m);
        var m = Montar(Activo, largo, corto);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        // Ventana de 1040 (la vista a 1280 sin el sidebar): UNIDAD pide 240 y cede al espacio libre (145).
        Assert.Equal(145, Columna(grilla, "UNIDAD").ActualWidth, 1);
        Assert.DoesNotContain(grilla.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);
        var cortada = CeldaDe(grilla, largo, "UNIDAD");
        Assert.True(TooltipSiSeCorta.EstaCortado(cortada));
        Assert.False(TooltipSiSeCortaTests.SeCancela(cortada));
        Assert.Equal(largo.UnidadMedidaNombre, ToolTip.GetTip(cortada));
        foreach (var header in new[] { "CÓDIGO", "NOMBRE", "COSTO", "UNIDAD", "CATEGORÍA" })
            Assert.True(TooltipSiSeCortaTests.SeCancela(CeldaDe(grilla, corto, header)), $"{header} de una celda que entra abre tooltip.");

        // Con UNIDAD cediendo, NOMBRE queda en su piso de 160 y un nombre largo también se corta: elipsis y tooltip.
        Assert.Equal(160, Columna(grilla, "NOMBRE").ActualWidth, 1);
        var nombre = CeldaDe(grilla, Activo, "NOMBRE");
        Assert.True(TooltipSiSeCorta.EstaCortado(nombre));
        Assert.False(TooltipSiSeCortaTests.SeCancela(nombre));
        Assert.Equal(Activo.Nombre, ToolTip.GetTip(nombre));
    }

    [AvaloniaTheory]
    [InlineData(1040, 145)]
    [InlineData(1126, 231)]
    public void CeldaCortada_LaGrillaNoScrollea_YUnidadTomaElEspacioLibre(double ancho, double unidad)
    {
        // La vista a 1280 y a 1366 sin el sidebar de 240, con los datos del mockup de la celda cortada.
        var m = Montar(new ProductosFake { Lista = ColumnaAutoTopeAdaptableTests.ConCeldaCortada() }, ancho);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        Assert.Equal(unidad, Columna(grilla, "UNIDAD").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "CATEGORÍA").ActualWidth, 1);
        Assert.DoesNotContain(grilla.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(1126)]
    public void DatosDelMockup_LaGrillaNoScrollea_YNingunaCeldaSeCorta(double ancho)
    {
        // Los productos del mockup de Productos a 1280 y a 1366 sin el sidebar: a 1280 las columnas suman exactamente
        // el ancho interno de la grilla (948), así que un píxel de más en una medida ya daría scroll horizontal.
        var productos = ColumnaAutoTopeAdaptableTests.Productos();
        var m = Montar(new ProductosFake { Lista = productos }, ancho);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        Assert.DoesNotContain(grilla.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);
        foreach (var p in productos)
            foreach (var header in new[] { "CÓDIGO", "NOMBRE", "COSTO", "UNIDAD", "CATEGORÍA" })
                Assert.False(TooltipSiSeCorta.EstaCortado(CeldaDe(grilla, p, header)), $"{header} de {p.Nombre} quedó cortado.");
    }

    // ── Arrastre manual del separador (bug B4, 2026-10-09) ─────────────────────────────────────────────

    private static void ArrastrarSeparador(Montaje m, string header, double dx)
    {
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var cabecera = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, header));
        var borde = cabecera.TranslatePoint(new Point(cabecera.Bounds.Width - 2, cabecera.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseMove(borde);
        m.Ventana.MouseDown(borde, MouseButton.Left);
        for (var paso = 1; paso <= 5; paso++)
        {
            m.Ventana.MouseMove(borde + new Point(dx * paso / 5, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }
        m.Ventana.MouseUp(borde + new Point(dx, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static bool HayScrollHorizontal(DataGrid grilla) =>
        grilla.GetVisualDescendants().OfType<ScrollBar>().Any(s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);

    private static string Anchos(DataGrid grilla) =>
        string.Join(", ", grilla.Columns.Select(c => $"{c.Header}={c.ActualWidth:F1}"));

    [AvaloniaTheory]
    [InlineData("UNIDAD", 80)]
    [InlineData("CÓDIGO", 80)]
    public void ArrastrarUnaColumnaMedida_LasEstrellasNoBajanDeSuPiso_YSiNoEntraHayScroll(string header, double dx)
    {
        // 1040 = la vista a 1280 con el sidebar. Con los datos del mockup las columnas suman justo el ancho interno
        // (948): agrandar una medida 80 px no entra y el scroll horizontal es lo que corresponde, no apretar a CATEGORÍA.
        var m = Montar(new ProductosFake { Lista = ColumnaAutoTopeAdaptableTests.Productos() });
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = Columna(grilla, header).ActualWidth;

        ArrastrarSeparador(m, header, dx);

        Assert.Equal(antes + dx, Columna(grilla, header).ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "CATEGORÍA").ActualWidth, 1);
        Assert.Equal(220, Columna(grilla, "STOCK").ActualWidth, 1);
        Assert.True(HayScrollHorizontal(grilla), $"No entra y no hay scroll horizontal: {Anchos(grilla)}");
    }

    [AvaloniaFact]
    public void ArrastrarNombre_CategoriaNoBajaDeSuPiso_YHayScroll()
    {
        var m = Montar(new ProductosFake { Lista = ColumnaAutoTopeAdaptableTests.Productos() });
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = Columna(grilla, "NOMBRE").ActualWidth;

        ArrastrarSeparador(m, "NOMBRE", 60);

        Assert.Equal(antes + 60, Columna(grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "CATEGORÍA").ActualWidth, 1);
        Assert.Equal(100, Columna(grilla, "COSTO").ActualWidth, 1);
        Assert.True(HayScrollHorizontal(grilla), $"No entra y no hay scroll horizontal: {Anchos(grilla)}");
    }

    [AvaloniaFact]
    public void ArrastrarUnidad_ElAnchoArrastradoPersisteAlBuscar_YLasEstrellasSiguenEnSuPiso()
    {
        var m = Montar(new ProductosFake { Lista = ColumnaAutoTopeAdaptableTests.Productos() });
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        ArrastrarSeparador(m, "UNIDAD", 80);
        var arrastrada = Columna(grilla, "UNIDAD").ActualWidth;

        m.Vm.FiltroBusqueda = "Pintura";
        m.Vm.BuscarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(arrastrada, Columna(grilla, "UNIDAD").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "CATEGORÍA").ActualWidth, 1);
    }

    // ── Gate de Nuevo / Editar / Eliminar por catalogo.productos (bug B3, 2026-10-09) ──────────────────
    // Operador de permisos mixtos: Admin cortocircuita el gate y no probaría nada.

    private static bool VisibleBoton(Montaje m, string nombre) =>
        ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Button>(m.Vista, nombre));

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    [AvaloniaFact]
    public void ConGestionarProductos_ElListadoMuestraNuevoEditarYEliminar()
    {
        var m = Montar(new ProductosFake { Lista = { Activo } }, sesion: Mixto(Permisos.GestionarProductos, Permisos.VerReportes));

        Assert.True(VisibleBoton(m, "BotonNuevo"));
        Assert.True(VisibleBoton(m, "BotonEditar"));
        Assert.True(VisibleBoton(m, "BotonEliminar"));
    }

    [AvaloniaFact]
    public void SinGestionarProductos_ElListadoOcultaNuevoEditarYEliminar()
    {
        var m = Montar(new ProductosFake { Lista = { Activo } }, sesion: Mixto(Permisos.VerReportes));

        Assert.False(VisibleBoton(m, "BotonNuevo"));
        Assert.False(VisibleBoton(m, "BotonEditar"));
        Assert.False(VisibleBoton(m, "BotonEliminar"));
    }

    [AvaloniaFact]
    public void RevocarGestionarProductos_EnCaliente_OcultaLosBotonesSinRecrearLaVista()
    {
        var sesion = Mixto(Permisos.GestionarProductos, Permisos.VerReportes);
        var m = Montar(new ProductosFake { Lista = { Activo } }, sesion: sesion);
        Assert.True(VisibleBoton(m, "BotonNuevo"), "Precondición: con el permiso, Nuevo se ve.");

        sesion.EstablecerPermisos(new HashSet<string> { Permisos.VerReportes });
        m.Vm.NotificarPermisosActualizados();
        Dispatcher.UIThread.RunJobs();

        Assert.False(VisibleBoton(m, "BotonNuevo"));
        Assert.False(VisibleBoton(m, "BotonEditar"));
        Assert.False(VisibleBoton(m, "BotonEliminar"));
    }

    [AvaloniaFact]
    public async Task Un403_ConLaPantallaDeProductosAbierta_OcultaElBotonNuevoDesdeElShell()
    {
        var sesion = Mixto(Permisos.GestionarProductos, Permisos.VerReportes);
        var tras = new HashSet<string> { Permisos.VerReportes };
        var shellVm = new ShellMainViewModel(
            sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var productos = new ProductoListViewModel(new ProductosFake { Lista = { Activo } }, new NavigationRecorderFake(),
            new ConfirmacionServiceFake(), sesion);
        shellVm.CurrentContent = productos;

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

        Control Nuevo() => ventana.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BotonNuevo");
        Assert.True(ArbolVisual.EsVisibleEnArbol(Nuevo()), "Precondición: con el permiso, Nuevo se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras),
            new ConfirmacionServiceFake(), shellVm.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(Nuevo()));
    }
}
