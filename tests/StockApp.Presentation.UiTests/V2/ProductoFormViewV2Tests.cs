using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de producto v2 (spec §4.4 y §5.2) con la View REAL, clicks reales y el tema real.</summary>
public class ProductoFormViewV2Tests
{
    private sealed class ProductosGrabador : IProductoService
    {
        public Exception? FallaAlGuardar { get; set; }
        public List<Producto> Altas { get; } = new();

        public Task<int> AltaAsync(Producto producto)
        {
            if (FallaAlGuardar is not null) throw FallaAlGuardar;
            Altas.Add(producto);
            return Task.FromResult(99);
        }

        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) => throw new NotSupportedException();
    }

    private sealed class UnidadesSinPermiso : IUnidadMedidaService
    {
        public Task<UnidadMedida> GarantizarUnidadPorDefectoAsync() => throw new UnauthorizedAccessException("403");
        public Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync() => throw new UnauthorizedAccessException("403");
        public Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync() => throw new NotSupportedException();
        public Task<int> AltaAsync(UnidadMedida unidadMedida) => throw new NotSupportedException();
        public Task ModificarAsync(UnidadMedida unidadMedida) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
    }

    private static readonly UnidadMedida Unidad = new() { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };

    private static readonly ProductoDto Existente = new(7, "MAT-0142", "7730123456789", "Cemento portland 50 kg", null,
        null, null, null, 1, "Unidad", 480m, 320m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private sealed record Montaje(Window Ventana, ProductoFormView Vista, ProductoFormViewModel Vm, ProductosGrabador Servicio,
        NavigationRecorderFake Navegacion);

    private static Montaje Montar(bool edicion = false, IUnidadMedidaService? unidades = null, IReadOnlyList<Categoria>? categorias = null)
    {
        var servicio = new ProductosGrabador();
        var navegacion = new NavigationRecorderFake();
        var vm = new ProductoFormViewModel(servicio, unidades ?? new UnidadMedidaServiceFake(new List<UnidadMedida> { Unidad }),
            new CategoriaServiceFake(categorias ?? new List<Categoria>()), navegacion);
        if (edicion) vm.CargarParaEditar(Existente);
        var vista = new ProductoFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
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

    private static Rect EnVentana(Window ventana, Control c) =>
        new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

    [AvaloniaFact]
    public void Raiz_TieneUi2_YBreadcrumbConElTituloDelVm()
    {
        var alta = Montar();
        var encabezado = MontajeV2.Parte<EncabezadoPagina>(alta.Vista, "Encabezado");

        Assert.Contains("ui2", alta.Vista.Classes);
        Assert.Equal("Productos", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo producto", encabezado.BreadcrumbActual);
        Assert.Equal("Nuevo producto", encabezado.Titulo);
        Assert.Same(alta.Vm.CancelarCommand, encabezado.BreadcrumbComando);

        Assert.Equal("Editar producto", MontajeV2.Parte<EncabezadoPagina>(Montar(edicion: true).Vista, "Encabezado").Titulo);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAlListadoSinGuardar()
    {
        var m = Montar();
        m.Vm.Codigo = "X-1";
        m.Vm.Nombre = "Algo";
        var origen = MontajeV2.Parte<EncabezadoPagina>(m.Vista, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(ProductoListViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Altas);
    }

    [AvaloniaFact]
    public void Tarjeta_Mide760YQuedaALaIzquierda()
    {
        var m = Montar();
        var tarjeta = EnVentana(m.Ventana, MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaFact]
    public void Campos_EnDosColumnasDondeCorresponde()
    {
        var m = Montar();
        Rect R(string nombre) => EnVentana(m.Ventana, MontajeV2.Parte<CampoFormulario>(m.Vista, nombre));

        Assert.Equal(R("CampoSku").Y, R("CampoCodigoBarras").Y);
        Assert.True(R("CampoCodigoBarras").X > R("CampoSku").Right);
        Assert.Equal(R("CampoSku").X, R("CampoNombre").X);
        Assert.Equal(R("CampoCodigoBarras").Right, R("CampoNombre").Right);
        Assert.Equal(R("CampoNombre").Width, R("CampoDescripcion").Width);
        Assert.Equal(R("CampoPrecioCosto").Y, R("CampoUnidad").Y);
        Assert.Equal(R("CampoPrecioCosto").X, R("CampoCategoria").X);
        Assert.True(R("CampoCategoria").Y > R("CampoPrecioCosto").Bottom);
    }

    [AvaloniaFact]
    public void Requeridos_SoloSkuYNombre()
    {
        var m = Montar();
        var requeridos = m.Vista.GetVisualDescendants().OfType<CampoFormulario>().Where(c => c.Requerido).Select(c => c.Etiqueta).ToList();

        Assert.Equal(new string?[] { "SKU", "Nombre" }, requeridos);
    }

    [AvaloniaFact]
    public void Edicion_SkuDeshabilitadoConTokensDeDeshabilitado()
    {
        var edicion = Montar(edicion: true);
        var sku = MontajeV2.Parte<CampoFormulario>(edicion.Vista, "CampoSku").GetVisualDescendants().OfType<TextBox>().First();

        Assert.False(sku.IsEffectivelyEnabled);
        Assert.Equal("MAT-0142", sku.Text);
        var borde = sku.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        Assert.Equal(Color.Parse("#F5F5F4"), Assert.IsAssignableFrom<ISolidColorBrush>(borde.Background).Color);
        Assert.Equal(Color.Parse("#E7E5E4"), Assert.IsAssignableFrom<ISolidColorBrush>(borde.BorderBrush).Color);

        var alta = Montar();
        Assert.True(MontajeV2.Parte<CampoFormulario>(alta.Vista, "CampoSku").GetVisualDescendants().OfType<TextBox>().First().IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Pie_CancelarSecundarioYGuardarUnicoPrimarioALaDerecha()
    {
        var m = Montar();
        var cancelar = MontajeV2.Parte<Button>(m.Vista, "BotonCancelar");
        var guardar = MontajeV2.Parte<Button>(m.Vista, "BotonGuardar");

        Assert.Contains("secundario", cancelar.Classes);
        Assert.Equal("Guardar", guardar.Content);
        Assert.Same(m.Vm.GuardarCommand, guardar.Command);
        Assert.True(EnVentana(m.Ventana, guardar).X > EnVentana(m.Ventana, cancelar).Right);
        Assert.Equal(EnVentana(m.Ventana, MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta")).Right - 1 - 24,
            EnVentana(m.Ventana, guardar).Right, 0);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Guardar_ClickReal_ConDatosValidos_DaDeAltaYVuelveAlListado()
    {
        var m = Montar();
        m.Vm.Codigo = "NUE-0001";
        m.Vm.Nombre = "Producto nuevo";
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonGuardar"));

        var alta = Assert.Single(m.Servicio.Altas);
        Assert.Equal("NUE-0001", alta.Codigo);
        Assert.Equal(typeof(ProductoListViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Guardar_ConErrorDelServicio_MuestraLaAlertaYNoNavega()
    {
        var m = Montar();
        m.Servicio.FallaAlGuardar = new ReglaDeNegocioException("Ya existe un producto con el código 'MAT-0142'.");
        m.Vm.Codigo = "MAT-0142";
        m.Vm.Nombre = "Cemento portland 50 kg";
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Alerta>(m.Vista, "AlertaError")));

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonGuardar"));

        var alerta = MontajeV2.Parte<Alerta>(m.Vista, "AlertaError");
        Assert.True(ArbolVisual.EsVisibleEnArbol(alerta));
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("Ya existe un producto con el código 'MAT-0142'.", alerta.Texto);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void SinPermiso_MuestraElEstadoEnLugarDeLaTarjeta()
    {
        var m = Montar(unidades: new UnidadesSinPermiso());

        Assert.True(m.Vm.SinPermiso);
        var estado = MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinPermiso");
        Assert.True(ArbolVisual.EsVisibleEnArbol(estado));
        Assert.True(estado.EsError);
        Assert.Equal("No tenés permiso para cargar los datos de este producto.", estado.Mensaje);
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<EncabezadoPagina>(m.Vista, "Encabezado")));
    }

    [AvaloniaFact]
    public void DesplegableDeCombo_AbiertoTieneCaltApagado()
    {
        var m = Montar(categorias: new List<Categoria> { new() { Id = 1, Nombre = "Cable 2x1,5 mm", Activo = true } });
        var combo = MontajeV2.Parte<CampoFormulario>(m.Vista, "CampoCategoria").GetVisualDescendants().OfType<ComboBox>().First();

        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var item = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
        var texto = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Cable 2x1,5 mm");
        var rasgos = texto.GetValue(TextElement.FontFeaturesProperty);
        Assert.True(rasgos is not null && rasgos.Any(f => f.Tag == "calt" && f.Value == 0),
            "El ítem del desplegable abierto se dibuja con calt: saldría \"2×1,5\".");
    }
}
