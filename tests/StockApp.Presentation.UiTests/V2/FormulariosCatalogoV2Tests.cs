using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Entities;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Los diez formularios de catálogo v2 con la View REAL: la MISMA estructura en los diez (breadcrumb que vuelve a su
/// catálogo, card de 760, un solo primario, error del servidor en una Alerta, asterisco solo donde Guardar lo exige) y lo
/// propio de Proveedor y de Línea POA.</summary>
public class FormulariosCatalogoV2Tests
{
    private sealed record Montaje(Window Ventana, Control Vista, ViewModelBase Vm, NavigationRecorderFake Nav);

    /// <summary>Un caso por formulario: cómo armarlo, cómo se llama su catálogo, cómo cargarlo en edición, y por cada campo
    /// con etiqueta la propiedad del VM que lo alimenta y un valor válido.</summary>
    private sealed record Caso(string Nombre, Func<NavigationRecorderFake, (Control Vista, ViewModelBase Vm)> Crear, string Origen,
        Action<ViewModelBase> Editar, Dictionary<string, (string Propiedad, string Valido)> Campos)
    {
        public override string ToString() => Nombre;
    }

    private static Dictionary<string, (string, string)> Nombre() => new() { ["Nombre"] = ("Nombre", "Algo") };

    private static readonly Caso[] Casos =
    [
        new("Categoria", n => { var vm = new CategoriaFormViewModel(new CategoriaServicioFake(), n); return (new CategoriaFormView { DataContext = vm }, vm); },
            "Categorías", vm => ((CategoriaFormViewModel)vm).CargarParaEditar(new Categoria { Id = 1, Nombre = "Electricidad" }), Nombre()),
        new("Proveedor", n => { var vm = new ProveedorFormViewModel(new ProveedorServicioFake(), n); return (new ProveedorFormView { DataContext = vm }, vm); },
            "Proveedores", vm => ((ProveedorFormViewModel)vm).CargarParaEditar(new Proveedor { Id = 1, Nombre = "Barraca Carmelo" }),
            new() { ["Nombre"] = ("Nombre", "Algo"), ["Teléfono"] = ("Telefono", "1"), ["Email"] = ("Email", "a@b.c"), ["Dirección"] = ("Direccion", "x"), ["Notas"] = ("Notas", "x") }),
        new("UnidadMedida", n => { var vm = new UnidadMedidaFormViewModel(new UnidadMedidaServicioFake(), n); return (new UnidadMedidaFormView { DataContext = vm }, vm); },
            "Unidades de medida", vm => ((UnidadMedidaFormViewModel)vm).CargarParaEditar(new UnidadMedida { Id = 1, Nombre = "Kilogramo", Abreviatura = "kg" }),
            new() { ["Nombre"] = ("Nombre", "Algo"), ["Abreviatura"] = ("Abreviatura", "x") }),
        new("FuenteFinanciamiento", n => { var vm = new FuenteFinanciamientoFormViewModel(new FuenteFinanciamientoServicioFake(), n); return (new FuenteFinanciamientoFormView { DataContext = vm }, vm); },
            "Fuentes de financiamiento", vm => ((FuenteFinanciamientoFormViewModel)vm).CargarParaEditar(new FuenteFinanciamiento { Id = 1, Nombre = "FIGM" }), Nombre()),
        new("RubroGasto", n => { var vm = new RubroGastoFormViewModel(new RubroGastoServicioFake(), n); return (new RubroGastoFormView { DataContext = vm }, vm); },
            "Rubros de gasto", vm => ((RubroGastoFormViewModel)vm).CargarParaEditar(new RubroGasto { Id = 1, Codigo = 12, Nombre = "Combustibles" }),
            new() { ["Código"] = ("CodigoTexto", "12"), ["Nombre"] = ("Nombre", "Algo") }),
        new("LineaPoa", n => { var vm = new LineaPoaFormViewModel(new LineaPoaServicioFake(), new FuenteFinanciamientoServicioFake(), n); return (new LineaPoaFormView { DataContext = vm }, vm); },
            "Líneas POA", vm => ((LineaPoaFormViewModel)vm).CargarParaEditar(new LineaPoa { Id = 1, Nombre = "Caminería", Programa = "Obras", Ejercicio = 2026 }),
            new() { ["Nombre"] = ("Nombre", "Algo"), ["Programa"] = ("Programa", "Obras"), ["Ejercicio"] = ("EjercicioTexto", "2026") }),
        new("Zona", n => { var vm = new ZonaFormViewModel(new ZonaServicioFake(), n); return (new ZonaFormView { DataContext = vm }, vm); },
            "Zonas", vm => ((ZonaFormViewModel)vm).CargarParaEditar(new Zona { Id = 1, Nombre = "Centro" }), Nombre()),
        new("DimensionTematica", n => { var vm = new DimensionTematicaFormViewModel(new DimensionTematicaServicioFake(), n); return (new DimensionTematicaFormView { DataContext = vm }, vm); },
            "Dimensiones", vm => ((DimensionTematicaFormViewModel)vm).CargarParaEditar(new DimensionTematica { Id = 1, Nombre = "Ambiente" }), Nombre()),
        new("OrganismoResponsable", n => { var vm = new OrganismoResponsableFormViewModel(new OrganismoResponsableServicioFake(), n); return (new OrganismoResponsableFormView { DataContext = vm }, vm); },
            "Organismos responsables", vm => ((OrganismoResponsableFormViewModel)vm).CargarParaEditar(new OrganismoResponsable { Id = 1, Nombre = "Obras" }), Nombre()),
        new("OrigenFinanciamiento", n => { var vm = new OrigenFinanciamientoFormViewModel(new OrigenFinanciamientoServicioFake(), n); return (new OrigenFinanciamientoFormView { DataContext = vm }, vm); },
            "Orígenes de financiamiento", vm => ((OrigenFinanciamientoFormViewModel)vm).CargarParaEditar(new OrigenFinanciamiento { Id = 1, Nombre = "Nacional" }), Nombre()),
    ];

    public static IEnumerable<object[]> TodosLosFormularios() => Enumerable.Range(0, Casos.Length).Select(i => new object[] { i });

    private static Montaje Montar(Caso caso, bool edicion = false, double ancho = 1040)
    {
        var nav = new NavigationRecorderFake();
        var (vista, vm) = caso.Crear(nav);
        if (edicion) caso.Editar(vm);
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, nav);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

    private static void Poner(object vm, string propiedad, string valor)
        => vm.GetType().GetProperty(propiedad)!.SetValue(vm, valor);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [MemberData(nameof(TodosLosFormularios))]
    public void Raiz_TieneUi2_BreadcrumbConElCatalogoYElTituloDelVm(int indice)
    {
        var caso = Casos[indice];
        foreach (var edicion in new[] { false, true })
        {
            var m = Montar(caso, edicion);
            var encabezado = Parte<EncabezadoPagina>(m, "Encabezado");
            var titulo = (string)m.Vm.GetType().GetProperty("Titulo")!.GetValue(m.Vm)!;

            Assert.Contains("ui2", m.Vista.Classes);
            Assert.Equal(caso.Origen, encabezado.BreadcrumbOrigen);
            Assert.Equal(titulo, encabezado.BreadcrumbActual);
            Assert.Equal(titulo, encabezado.Titulo);
            Assert.Equal(edicion, titulo.StartsWith("Editar", StringComparison.Ordinal));
            m.Ventana.Close();
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(TodosLosFormularios))]
    public void Estructura_CardDe760_GuardarEsElUnicoPrimario_CancelarSecundario(int indice)
    {
        var caso = Casos[indice];
        var m = Montar(caso);
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));
        var guardar = Parte<Button>(m, "BotonGuardar");
        var cancelar = Parte<Button>(m, "BotonCancelar");

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
        Assert.Contains("primario", guardar.Classes);
        Assert.Contains("secundario", cancelar.Classes);
        Assert.Equal("Guardar", guardar.Content);
        Assert.Equal("Cancelar", cancelar.Content);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<TabControl>());
    }

    [AvaloniaTheory]
    [MemberData(nameof(TodosLosFormularios))]
    public void AlertaError_SoloConMensajeError(int indice)
    {
        var caso = Casos[indice];
        var m = Montar(caso);
        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.False(Visible(alerta));

        m.Vm.GetType().GetProperty("MensajeError")!.SetValue(m.Vm, "Ya existe uno con ese nombre.");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(alerta));
        Assert.Equal("Ya existe uno con ese nombre.", alerta.Texto);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
    }

    [AvaloniaTheory]
    [MemberData(nameof(TodosLosFormularios))]
    public void Breadcrumb_VuelveACatalogosConElCatalogoDelFormulario(int indice)
    {
        var caso = Casos[indice];
        var m = Montar(caso);
        var origen = Parte<EncabezadoPagina>(m, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(CatalogosViewModel), m.Nav.UltimoTipoNavegado);
        Assert.NotNull(m.Nav.UltimoInicializador);
    }

    [AvaloniaTheory]
    [MemberData(nameof(TodosLosFormularios))]
    public void Asteriscos_SoloEnLoQueExigePuedeGuardar(int indice)
    {
        var caso = Casos[indice];
        var m = Montar(caso);
        var guardar = Parte<Button>(m, "BotonGuardar");
        var campos = m.Vista.GetVisualDescendants().OfType<CampoFormulario>().ToList();
        Assert.Equal(caso.Campos.Keys.OrderBy(k => k), campos.Select(c => c.Etiqueta!).OrderBy(k => k));

        foreach (var (_, (prop, valido)) in caso.Campos)
            Poner(m.Vm, prop, valido);
        Dispatcher.UIThread.RunJobs();
        Assert.True(guardar.Command!.CanExecute(null), "Con todos los campos válidos Guardar tiene que poder ejecutarse.");

        foreach (var campo in campos)
        {
            var (prop, valido) = caso.Campos[campo.Etiqueta!];
            Poner(m.Vm, prop, "");
            Dispatcher.UIThread.RunJobs();
            var guardarPosible = guardar.Command!.CanExecute(null);

            Assert.True(campo.Requerido == !guardarPosible,
                $"{caso.Nombre}: el campo '{campo.Etiqueta}' dice Requerido={campo.Requerido} pero vaciarlo deja Guardar {(guardarPosible ? "habilitado" : "deshabilitado")}.");
            Poner(m.Vm, prop, valido);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Caso De(string nombre) => Casos.Single(c => c.Nombre == nombre);

    [AvaloniaFact]
    public void Proveedor_GrillaDeCampos_TelefonoYEmailEnUnaFila()
    {
        var m = Montar(De("Proveedor"));
        Rect Campo(string etiqueta) => EnVentana(m.Ventana, m.Vista.GetVisualDescendants().OfType<CampoFormulario>().Single(c => c.Etiqueta == etiqueta));

        var nombre = Campo("Nombre"); var tel = Campo("Teléfono"); var email = Campo("Email"); var dir = Campo("Dirección"); var notas = Campo("Notas");

        Assert.Equal(tel.Y, email.Y);
        Assert.True(email.X > tel.Right);
        Assert.Equal(tel.Width, email.Width, 1);
        Assert.Equal(nombre.Width, dir.Width);
        Assert.Equal(nombre.Width, notas.Width);
        Assert.True(tel.Y > nombre.Y && dir.Y > tel.Y && notas.Y > dir.Y);
        Assert.Equal(60, m.Vista.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CajaNotas").Height);
    }

    [AvaloniaFact]
    public void LineaPoa_DosSecciones_YFilasDeAsignacion()
    {
        var m = Montar(De("LineaPoa"), edicion: true);
        var linea = (LineaPoaFormViewModel)m.Vm;
        var subtitulos = m.Vista.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("subtitulo-formulario")).Select(t => t.Text).ToArray();

        Assert.Equal(new[] { "Datos de la línea", "Asignaciones presupuestales" }, subtitulos);
        var encabezados = m.Vista.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Fuente de financiamiento", encabezados);
        Assert.Contains("Monto", encabezados);

        linea.AgregarAsignacionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var filas = Parte<ItemsControl>(m, "Asignaciones").GetVisualDescendants().OfType<ComboBox>().Count();
        Assert.Equal(linea.Asignaciones.Count, filas);
        Assert.Equal("Agregar asignación", Parte<Button>(m, "BotonAgregarAsignacion").Content);
        Assert.Contains("secundario", Parte<Button>(m, "BotonAgregarAsignacion").Classes);
    }

    [AvaloniaFact]
    public void LineaPoa_QuitarAsignacion_EsGhostIcono()
    {
        var m = Montar(De("LineaPoa"));
        var linea = (LineaPoaFormViewModel)m.Vm;
        linea.AgregarAsignacionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var antes = linea.Asignaciones.Count;

        var quitar = Parte<ItemsControl>(m, "Asignaciones").GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("ghost"));

        Assert.Contains("icono", quitar.Classes);
        Assert.IsAssignableFrom<Avalonia.Controls.Control>(quitar.Content);
        Assert.Null(quitar.Content as string);
        Clickear(m.Ventana, quitar);
        Assert.Equal(antes - 1, linea.Asignaciones.Count);
    }

    [AvaloniaFact]
    public void LineaPoa_SinPermiso_ReemplazaLaCard()
    {
        var fuentes = new FuenteFinanciamientoServicioFake { Falla = new UnauthorizedAccessException() };
        var nav = new NavigationRecorderFake();
        var vm = new LineaPoaFormViewModel(new LineaPoaServicioFake(), fuentes, nav);
        var vista = new LineaPoaFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.SinPermiso);
        Assert.True(Visible(MontajeV2.Parte<EstadoVacio>(vista, "EstadoSinPermiso")));
        Assert.False(Visible(MontajeV2.Parte<TarjetaContenido>(vista, "Tarjeta")));
    }
}
