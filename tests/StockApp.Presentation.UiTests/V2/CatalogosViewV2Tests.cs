using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Administracion;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Catálogos v2 con las Views REALES (CatalogosView y la CatalogoListadoView genérica): estructura, master con gates por
/// grupo (con operadores de permisos mixtos), columnas por catálogo, estados, búsqueda, doble click y revocación en caliente.</summary>
public class CatalogosViewV2Tests
{
    private sealed class Servicios
    {
        public CategoriaServicioFake Categorias { get; } = new();
        public ProveedorServicioFake Proveedores { get; } = new();
        public UnidadMedidaServicioFake Unidades { get; } = new();
        public FuenteFinanciamientoServicioFake Fuentes { get; } = new();
        public RubroGastoServicioFake Rubros { get; } = new();
        public LineaPoaServicioFake Lineas { get; } = new();
        public ZonaServicioFake Zonas { get; } = new();
        public DimensionTematicaServicioFake Dimensiones { get; } = new();
        public OrganismoResponsableServicioFake Organismos { get; } = new();
        public OrigenFinanciamientoServicioFake Origenes { get; } = new();
    }

    private static Categoria C(int id, string nombre, bool activo = true) => new() { Id = id, Nombre = nombre, Activo = activo };

    private static CatalogosViewModel Armar(Servicios s, SesionFake sesion, NavigationRecorderFake? nav = null, ConfirmacionServiceFake? conf = null)
    {
        nav ??= new NavigationRecorderFake();
        conf ??= new ConfirmacionServiceFake();
        return new CatalogosViewModel(sesion,
            new CategoriaListViewModel(s.Categorias, nav, conf), new ProveedorListViewModel(s.Proveedores, nav, conf),
            new UnidadMedidaListViewModel(s.Unidades, nav, conf), new FuenteFinanciamientoListViewModel(s.Fuentes, nav, conf),
            new RubroGastoListViewModel(s.Rubros, nav, conf), new LineaPoaListViewModel(s.Lineas, nav, conf),
            new ZonaListViewModel(s.Zonas, nav, conf), new DimensionTematicaListViewModel(s.Dimensiones, nav, conf),
            new OrganismoResponsableListViewModel(s.Organismos, nav, conf), new OrigenFinanciamientoListViewModel(s.Origenes, nav, conf));
    }

    private static SesionFake Admin() => new(RolUsuario.Admin);
    private static SesionFake SoloTablas() => new(RolUsuario.Operador, Permisos.GestionarTablasMaestras, Permisos.VerFinanzas);
    private static SesionFake SoloFinanzas() => new(RolUsuario.Operador, Permisos.GestionarMaestrosFinanzas, Permisos.GestionarProductos);

    private sealed record Montaje(Window Ventana, CatalogosView Vista, CatalogosViewModel Vm, Servicios Servicios,
        NavigationRecorderFake Nav, ConfirmacionServiceFake Conf);

    private static Montaje Montar(Servicios? servicios = null, SesionFake? sesion = null, ClaveCatalogo? abrir = null, double ancho = 1040)
    {
        servicios ??= new Servicios();
        var nav = new NavigationRecorderFake();
        var conf = new ConfirmacionServiceFake();
        var vm = Armar(servicios, sesion ?? Admin(), nav, conf);
        if (abrir is { } clave) vm.Seleccionar(clave);
        var vista = new CatalogosView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicios, nav, conf);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);

    private static IEnumerable<ItemSidebar> Items(Montaje m) =>
        Control<TarjetaContenido>(m, "Master").GetVisualDescendants().OfType<ItemSidebar>();

    private static ItemSidebar Item(Montaje m, string titulo) => Items(m).Single(i => i.Titulo == titulo);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void DobleClick(Window ventana, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 2; i++)
        {
            ventana.MouseDown(p, MouseButton.Left);
            ventana.MouseUp(p, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridRow FilaDe(Montaje m, object entidad) =>
        m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is FilaCatalogo f && ReferenceEquals(f.Entidad, entidad));

    private static DataGrid Grilla(Montaje m) => Control<DataGrid>(m, "Grilla");

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoCatalogos_YNuevaEsElUnicoPrimario()
    {
        var s = new Servicios();
        s.Categorias.Lista = [C(1, "Electricidad")];
        var m = Montar(s);
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Catálogos", encabezado.Titulo);
        Assert.Equal("Tablas maestras del sistema.", encabezado.Subtitulo);
        Assert.Contains("primario", Control<Button>(m, "BotonNuevo").Classes);
        Assert.Equal("Nueva categoría", Control<Button>(m, "BotonNuevo").Content);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
    }

    [AvaloniaFact]
    public void Master_Admin_MuestraLosTresGruposConSusDiezItemsEnOrden()
    {
        var m = Montar();

        Assert.True(Visible(Control<StackPanel>(m, "GrupoInventario")));
        Assert.True(Visible(Control<StackPanel>(m, "GrupoFinanzas")));
        Assert.True(Visible(Control<StackPanel>(m, "GrupoTareas")));
        Assert.Equal(new[]
        {
            "Categorías", "Proveedores", "Unidades de medida", "Fuentes de financiamiento", "Rubros de gasto", "Líneas POA",
            "Zonas", "Dimensiones", "Organismos responsables", "Orígenes de financiamiento",
        }, Items(m).Select(i => i.Titulo).ToArray());
        Assert.Equal("mdi-shape", Item(m, "Categorías").Icono);
        Assert.Equal("mdi-bank", Item(m, "Orígenes de financiamiento").Icono);
    }

    [AvaloniaFact]
    public void Master_OperadorConTablasMaestras_OcultaFinanzasConSuHeader()
    {
        var m = Montar(sesion: SoloTablas());

        Assert.True(Visible(Control<StackPanel>(m, "GrupoInventario")));
        Assert.False(Visible(Control<StackPanel>(m, "GrupoFinanzas")));
        Assert.True(Visible(Control<StackPanel>(m, "GrupoTareas")));
        Assert.DoesNotContain(Items(m), i => i.Titulo == "Líneas POA" && Visible(i));
        Assert.Equal("Categorías", Control<TextBlock>(m, "TituloCatalogo").Text);
    }

    [AvaloniaFact]
    public void Master_OperadorConMaestrosFinanzas_SoloFinanzas()
    {
        var m = Montar(sesion: SoloFinanzas());

        Assert.False(Visible(Control<StackPanel>(m, "GrupoInventario")));
        Assert.True(Visible(Control<StackPanel>(m, "GrupoFinanzas")));
        Assert.False(Visible(Control<StackPanel>(m, "GrupoTareas")));
        Assert.Equal("Fuentes de financiamiento", Control<TextBlock>(m, "TituloCatalogo").Text);
        Assert.True(Item(m, "Fuentes de financiamiento").EstaActivo);
    }

    [AvaloniaFact]
    public void Master_ClickEnUnItem_LoMarcaActivoYCambiaElDetalle()
    {
        var s = new Servicios();
        s.Zonas.Lista = [new Zona { Id = 1, Nombre = "Centro" }];
        var m = Montar(s);
        Assert.True(Item(m, "Categorías").EstaActivo);

        Clickear(m.Ventana, Item(m, "Zonas"));

        Assert.True(Item(m, "Zonas").EstaActivo);
        Assert.False(Item(m, "Categorías").EstaActivo);
        Assert.Equal("Zonas", Control<TextBlock>(m, "TituloCatalogo").Text);
        Assert.Equal("Nueva zona", Control<Button>(m, "BotonNuevo").Content);
        Assert.Equal(1, s.Zonas.Consultas);
    }

    [AvaloniaFact]
    public void Detalle_Proveedores_MuestraLasCincoColumnasConSusEncabezados_YOcultaLasDemas()
    {
        var s = new Servicios();
        s.Proveedores.Lista = [new Proveedor { Id = 1, Nombre = "Barraca Carmelo", Telefono = "4542 2210", Email = "v@b.uy", Direccion = "18 de Julio", Notas = "n" }];
        var m = Montar(s, abrir: ClaveCatalogo.Proveedores);
        var columnas = Grilla(m).Columns;

        Assert.Equal(new object?[] { "NOMBRE", "TELÉFONO", "EMAIL", "DIRECCIÓN", "NOTAS", "ESTADO" }, columnas.Select(c => c.Header).ToArray());
        Assert.All(columnas, c => Assert.True(c.IsVisible));
    }

    [AvaloniaFact]
    public void Detalle_Categorias_SoloNombreYEstado()
    {
        var s = new Servicios();
        s.Categorias.Lista = [C(1, "Electricidad")];
        var m = Montar(s);
        var columnas = Grilla(m).Columns;

        Assert.Equal(new[] { true, false, false, false, false, true }, columnas.Select(c => c.IsVisible).ToArray());
        Assert.Equal("NOMBRE", columnas[0].Header);
        Assert.Equal("ESTADO", columnas[5].Header);
    }

    [AvaloniaFact]
    public void Detalle_Rubros_CodigoVaAntesQueNombre()
    {
        var s = new Servicios();
        s.Rubros.Lista = [new RubroGasto { Id = 1, Codigo = 12, Nombre = "Combustibles" }];
        var m = Montar(s, abrir: ClaveCatalogo.RubrosGasto);
        var columnas = Grilla(m).Columns;

        Assert.Equal("CÓDIGO", columnas[1].Header);
        Assert.True(columnas[1].DisplayIndex < columnas[0].DisplayIndex);
        var orden = columnas.OrderBy(c => c.DisplayIndex).Where(c => c.IsVisible).Select(c => c.Header).ToArray();
        Assert.Equal(new object?[] { "CÓDIGO", "NOMBRE", "ESTADO" }, orden);

        Clickear(m.Ventana, Item(m, "Zonas"));
        Assert.Equal(0, columnas[0].DisplayIndex);
    }

    [AvaloniaFact]
    public void Detalle_FilaInactiva_InsigniaConElGeneroDelCatalogo_YTextoInactivo()
    {
        var s = new Servicios();
        var baja = C(2, "Herramientas fuera de uso", activo: false);
        s.Categorias.Lista = [C(1, "Electricidad"), baja];
        s.Proveedores.Lista = [new Proveedor { Id = 1, Nombre = "Barraca", Activo = false }];
        var m = Montar(s);

        var insignia = FilaDe(m, baja).GetVisualDescendants().OfType<Insignia>().First(i => Visible(i));
        Assert.Equal("Inactiva", insignia.Texto);
        var nombre = FilaDe(m, baja).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Herramientas fuera de uso");
        Assert.Contains("inactivo", nombre.Classes);

        Clickear(m.Ventana, Item(m, "Proveedores"));
        var prov = m.Vista.GetVisualDescendants().OfType<DataGridRow>().First(r => Visible(r) && r.DataContext is FilaCatalogo f && f.Valor0 == "Barraca");
        Assert.Equal("Inactivo", prov.GetVisualDescendants().OfType<Insignia>().First(i => Visible(i)).Texto);
    }

    [AvaloniaFact]
    public void Detalle_Cargando_MuestraIndicadorSinGrillaNiPie()
    {
        var s = new Servicios();
        s.Categorias.Pendiente = new TaskCompletionSource<IReadOnlyList<Categoria>>();
        var m = Montar(s);

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.Equal("Cargando categorías…", Control<EstadoCargando>(m, "IndicadorCarga").Texto);
        Assert.False(Visible(Grilla(m)));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinDatos")));
    }

    [AvaloniaFact]
    public void Detalle_SinConexion_EstadoConReintentar_DentroDeLaCard()
    {
        var s = new Servicios();
        s.Categorias.Falla = new ServidorNoDisponibleException();
        var m = Montar(s);

        var estado = Control<EstadoVacio>(m, "EstadoSinConexion");
        Assert.True(Visible(estado));
        Assert.True(estado.GetVisualAncestors().Contains(Control<TarjetaContenido>(m, "Tarjeta")));
        Assert.True(Visible(Control<Button>(m, "BotonReintentar")));
        Assert.False(Visible(Grilla(m)));
        Assert.Empty(m.Conf.MensajesInformados);

        s.Categorias.Falla = null;
        s.Categorias.Lista = [C(1, "Electricidad")];
        Clickear(m.Ventana, Control<Button>(m, "BotonReintentar"));
        Assert.False(Visible(estado));
        Assert.True(Visible(Grilla(m)));
    }

    [AvaloniaFact]
    public void Detalle_SinPermiso_ReemplazaLaCardYElMasterQueda()
    {
        var s = new Servicios();
        s.Fuentes.Falla = new UnauthorizedAccessException();
        var m = Montar(s, abrir: ClaveCatalogo.FuentesFinanciamiento);

        var estado = Control<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(estado) || m.Vista.GetVisualDescendants().OfType<EstadoVacio>().Any(e => e.Name == "EstadoSinPermiso" && Visible(e)));
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
        Assert.True(Visible(Control<TarjetaContenido>(m, "Master")));
        Assert.Equal("No tenés permiso para ver las fuentes de financiamiento.", m.Vm.CatalogoActual!.MensajeSinPermiso);
    }

    [AvaloniaFact]
    public void Detalle_SinDatos_VacioConBotonNuevoSecundario()
    {
        var m = Montar(abrir: ClaveCatalogo.UnidadesMedida);

        var vacio = Control<EstadoVacio>(m, "EstadoSinDatos");
        Assert.True(Visible(vacio));
        Assert.Equal("Todavía no hay unidades de medida", vacio.Titulo);
        Assert.Equal("Tocá Nueva unidad para cargar el primero.", vacio.Mensaje);
        var boton = Control<Button>(m, "BotonNuevoVacio");
        Assert.Contains("secundario", boton.Classes);
        Assert.Equal("Nueva unidad", boton.Content);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
    }

    [AvaloniaFact]
    public void Detalle_SinResultados_NoMuestraElVacioDeSinDatos()
    {
        var s = new Servicios();
        s.Categorias.Lista = [C(1, "Electricidad")];
        var m = Montar(s);

        m.Vm.CatalogoActual!.Busqueda = "zzz";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.Equal("Nada coincide con la búsqueda", Control<EstadoVacio>(m, "EstadoSinResultados").Titulo);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinDatos")));
        Assert.False(Visible(Grilla(m)));
    }

    [AvaloniaFact]
    public void Busqueda_EscribirEnElBuscador_FiltraLaGrillaYElPie()
    {
        var s = new Servicios();
        s.Categorias.Lista = [C(1, "Electricidad"), C(2, "Plomería"), C(3, "Pinturería")];
        var m = Montar(s);
        var caja = Control<BarraFiltros>(m, "Filtros").GetVisualDescendants().OfType<TextBox>().First();

        caja.Text = "PLOMERIA";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("PLOMERIA", m.Vm.CatalogoActual!.Busqueda);
        Assert.Equal(1, m.Vm.CatalogoActual.CantidadVisible);
        Assert.Equal("1 categoría", Control<TextBlock>(m, "PieConteo").Text);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<DataGridRow>(), r => Visible(r));
    }

    [AvaloniaFact]
    public void Pie_CuentaLasVisibles_ConSingularYPlural()
    {
        var s = new Servicios();
        s.Categorias.Lista = [C(1, "Electricidad"), C(2, "Plomería")];
        s.Lineas.Lista = [new LineaPoa { Id = 1, Nombre = "Caminería", Programa = "Obras", Ejercicio = 2026 }];
        var m = Montar(s);
        Assert.Equal("2 categorías", Control<TextBlock>(m, "PieConteo").Text);

        Clickear(m.Ventana, Item(m, "Líneas POA"));
        Assert.Equal("1 línea POA", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void DobleClickEnFila_ActivaEdita()
    {
        var s = new Servicios();
        var activa = C(1, "Electricidad");
        s.Categorias.Lista = [activa, C(2, "Vieja", activo: false)];
        var m = Montar(s);

        DobleClick(m.Ventana, FilaDe(m, activa));

        Assert.NotNull(m.Nav.UltimoInicializador);
        Assert.Equal(typeof(CategoriaFormViewModel), m.Nav.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void DobleClickEnFila_InactivaNoEdita()
    {
        var s = new Servicios();
        var baja = C(2, "Vieja", activo: false);
        s.Categorias.Lista = [C(1, "Electricidad"), baja];
        var m = Montar(s);

        DobleClick(m.Ventana, FilaDe(m, baja));

        Assert.Null(m.Nav.UltimoInicializador);
        Assert.Null(m.Nav.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void DobleClickEnElEncabezado_NoEdita()
    {
        var s = new Servicios();
        var activa = C(1, "Electricidad");
        s.Categorias.Lista = [activa];
        var m = Montar(s);
        Clickear(m.Ventana, FilaDe(m, activa));

        var encabezado = Grilla(m).GetVisualDescendants().OfType<DataGridColumnHeader>().First();
        DobleClick(m.Ventana, encabezado);

        Assert.Null(m.Nav.UltimoInicializador);
    }

    [AvaloniaFact]
    public async Task RevocacionDeMaestrosFinanzas_DentroDelShell_ElGrupoDesapareceYElDetalleCambia()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.GestionarTablasMaestras, Permisos.GestionarMaestrosFinanzas);
        var tras = new HashSet<string> { Permisos.GestionarTablasMaestras };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var servicios = new Servicios();
        servicios.Categorias.Lista = [C(1, "Electricidad")];
        var vm = Armar(servicios, sesion);
        vm.Seleccionar(ClaveCatalogo.LineasPoa);
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<CatalogosView>().Single();
        Assert.True(Visible(MontajeV2.Parte<StackPanel>(vista, "GrupoFinanzas")), "Precondición: con finanzas.maestros se ve.");
        Assert.Equal("Líneas POA", MontajeV2.Parte<TextBlock>(vista, "TituloCatalogo").Text);

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(MontajeV2.Parte<StackPanel>(vista, "GrupoFinanzas")));
        Assert.Equal("Categorías", MontajeV2.Parte<TextBlock>(vista, "TituloCatalogo").Text);
        Assert.Same(vm, shell.CurrentContent);
    }
}
