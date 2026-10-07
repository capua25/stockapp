using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
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

        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();

        public Task BajaLogicaAsync(int id)
        {
            Bajas.Add(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Lista.ToList());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Lista
                .Where(p => string.IsNullOrEmpty(texto) || p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());
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

    private static Montaje Montar(params ProductoDto[] productos)
    {
        var servicio = new ProductosFake { Lista = productos.ToList() };
        var navegacion = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new ProductoListViewModel(servicio, navegacion, confirmacion);
        var vista = new ProductoListView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
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
}
