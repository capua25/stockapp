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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Ingresos de caja v2 con la View REAL: estructura, columnas, filtros y pie, estados, inactivos por token,
/// gates con un Operador de permisos mixtos, doble click, baja y 403 en caliente por el camino del shell.</summary>
public class IngresosViewV2Tests
{
    private static readonly Color Terciario = Color.Parse("#78716C");

    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };

    private static IngresoCaja I(int id, string concepto, decimal monto, bool activo = true, int dia = 6) => new()
    {
        Id = id, Fecha = new DateTime(2026, 10, dia, 0, 0, 0, DateTimeKind.Utc), Concepto = concepto,
        FuenteFinanciamientoId = 1, FuenteFinanciamiento = Figm, Monto = monto, Activo = activo,
    };

    private static readonly IngresoCaja Partida = I(1, "Partida mensual FIGM octubre", 250_000m);
    private static readonly IngresoCaja Multas = I(2, "Multas de tránsito", 1_000m, dia: 2);
    private static readonly IngresoCaja Duplicado = I(3, "Multa duplicada", 5_000m, activo: false, dia: 1);

    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarIngresos);
    private static SesionFake Mixto() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, IngresosView Vista, IngresosViewModel Vm, IngresoCajaFake Servicio,
        NavigationRecorderFake Navegacion, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(IngresoCajaFake servicio, SesionFake? sesion = null, double ancho = 1040)
    {
        var navegacion = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new IngresosViewModel(servicio, sesion ?? ConPermiso(), navegacion, confirmacion);
        var vista = new IngresosView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion, confirmacion);
    }

    private static Montaje Montar(params IngresoCaja[] ingresos) => Montar(new IngresoCajaFake { Lista = ingresos.ToList() });

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);

    private static DataGridRow Fila(Montaje m, IngresoCaja ingreso) =>
        m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, ingreso));

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Headless no lleva clickCount: el doble toque es la secuencia de dos clicks seguidos. El puntero entra
    /// primero, como el de un usuario: sin eso el hover del encabezado cambia el elemento bajo el puntero entre el primer
    /// click y el segundo, el doble toque no se dispara y un test sobre el encabezado pasaría sin custodiar nada.</summary>
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

    private static TextBlock CeldaConTexto(Montaje m, string texto) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto);

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_UnSoloPrimario()
    {
        var m = Montar(Partida);
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Ingresos de caja", encabezado.Titulo);
        Assert.Equal("Partidas, multas, préstamos y demás entradas de la caja municipal.", encabezado.Subtitulo);
        Assert.Contains("primario", Control<Button>(m, "BotonNuevo").Classes);
        Assert.Contains("secundario", Control<Button>(m, "BotonEditar").Classes);
        Assert.Contains("secundario", Control<Button>(m, "BotonBaja").Classes);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
    }

    [AvaloniaFact]
    public void Columnas_EnOrdenYEnMayusculas_SinCheckDeActivo()
    {
        var m = Montar(Partida);
        var grilla = Control<DataGrid>(m, "Grilla");

        Assert.Equal(new object?[] { "FECHA", "CONCEPTO", "CAJA", "FUENTE", "MONTO" }, grilla.Columns.Select(c => c.Header).ToArray());
        Assert.DoesNotContain(grilla.Columns, c => c is DataGridCheckBoxColumn);
        Assert.True(grilla.CanUserSortColumns);
    }

    [AvaloniaFact]
    public void Grilla_ColumnaCaja_MuestraPOA()
    {
        var poa = I(4, "Aporte POA obras", 90_000m);
        poa.Caja = Caja.Poa;
        var m = Montar(Partida, poa);

        Assert.True(Visible(CeldaConTexto(m, "POA")));
        Assert.True(Visible(CeldaConTexto(m, "Local")));
    }

    [AvaloniaFact]
    public void Fecha_DeMedianocheUtc_SeMuestraSinCorrerElDia_YMontoEnEsUy()
    {
        var m = Montar(Partida);

        Assert.True(Visible(CeldaConTexto(m, "06/10/2026")));
        Assert.True(Visible(CeldaConTexto(m, "$ 250.000,00")));
    }

    [AvaloniaFact]
    public void PorDefecto_SoloActivos_YPieConTotal()
    {
        var m = Montar(Partida, Multas, Duplicado);

        Assert.Equal("Activos", Control<ComboBox>(m, "ComboEstado").SelectedItem);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, Duplicado));
        Assert.Equal("2 ingresos · Total $ 251.000,00", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void ComboTodos_MuestraLosDadosDeBaja_ConTextoTerciarioEInsigniaSinOpacidad()
    {
        var m = Montar(Partida, Duplicado);

        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Todos";
        Dispatcher.UIThread.RunJobs();

        var fila = Fila(m, Duplicado);
        var textos = fila.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null).ToList();
        Assert.Equal(5, textos.Count); // fecha, concepto, caja, fuente, monto
        Assert.All(textos, t => Assert.Equal(Terciario, Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color));
        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Inactivo");
        Assert.True(Visible(insignia));
        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);
        var grilla = Control<DataGrid>(m, "Grilla");
        foreach (var v in textos.Cast<Visual>().Append(insignia))
            for (Visual? a = v; a is not null && !ReferenceEquals(a, grilla); a = a.GetVisualParent())
                Assert.True(a.Opacity == 1, $"{a.GetType().Name} con Opacity {a.Opacity} en la fila inactiva");
        Assert.Equal("2 ingresos · 1 dado de baja · Total $ 250.000,00", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void InsigniaInactivo_QuedaOchoPixelesDespuesDelConcepto()
    {
        var m = Montar(Duplicado);
        m.Vm.MostrarInactivos = true;
        Dispatcher.UIThread.RunJobs();

        var fila = Fila(m, Duplicado);
        var texto = fila.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Multa duplicada");
        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Inactivo");
        var finTexto = texto.TranslatePoint(new Point(texto.Bounds.Width, 0), m.Ventana)!.Value.X;
        var inicioInsignia = insignia.TranslatePoint(new Point(0, 0), m.Ventana)!.Value.X;

        Assert.Equal(finTexto + 8, inicioInsignia, 1);
    }

    [AvaloniaFact]
    public void Buscador_FiltraPorConcepto_YActualizaElPie()
    {
        var m = Montar(Partida, Multas);
        var caja = Control<BarraFiltros>(m, "Filtros").GetVisualDescendants().OfType<TextBox>().First();

        caja.Text = "multas";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("multas", m.Vm.TextoBusqueda);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<DataGridRow>(), r => Visible(r));
        Assert.Equal("1 ingreso · Total $ 1.000,00", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void Cargando_IndicadorEnLaCard_ConLosFiltrosAMano()
    {
        var m = Montar(new IngresoCajaFake { Pendiente = new TaskCompletionSource<IReadOnlyList<IngresoCaja>>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<BarraFiltros>(m, "Filtros")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinIngresos")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public void SinDatos_VacioConBotonNuevoSecundario()
    {
        var m = Montar();

        var vacio = Control<EstadoVacio>(m, "EstadoSinIngresos");
        Assert.True(Visible(vacio));
        Assert.Equal("Todavía no hay ingresos", vacio.Titulo);
        var boton = Control<Button>(m, "BotonNuevoVacio");
        Assert.True(Visible(boton));
        Assert.Contains("secundario", boton.Classes);
        Assert.Same(m.Vm.NuevoCommand, boton.Command);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
    }

    [AvaloniaFact]
    public void SinResultados_SuVacioYNoElDeSinDatos()
    {
        var m = Montar(Partida);

        m.Vm.TextoBusqueda = "donación";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.Equal("Ningún ingreso coincide con la búsqueda", Control<EstadoVacio>(m, "EstadoSinResultados").Titulo);
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinIngresos")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new IngresoCajaFake { Falla = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinIngresos")));

        servicio.Falla = null;
        servicio.Lista = [Partida];
        Clickear(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard()
    {
        var m = Montar(new IngresoCajaFake { Falla = new UnauthorizedAccessException("403") });

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.Equal(IngresosViewModel.MensajeSinPermisoIngresos, Control<EstadoVacio>(m, "EstadoSinPermiso").Mensaje);
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
    }

    [AvaloniaFact]
    public void Gates_OperadorMixto_SinAccionesYConSoloLectura()
    {
        var m = Montar(new IngresoCajaFake(), Mixto());

        Assert.False(Visible(Control<Button>(m, "BotonNuevo")));
        Assert.False(Visible(Control<Button>(m, "BotonEditar")));
        Assert.False(Visible(Control<Button>(m, "BotonBaja")));
        Assert.False(Visible(Control<Button>(m, "BotonNuevoVacio")));
        var insignia = Control<Insignia>(m, "InsigniaSoloLectura");
        Assert.True(Visible(insignia));
        Assert.Equal("Solo lectura", insignia.Texto);
    }

    [AvaloniaFact]
    public void Gates_OperadorConRegistrar_ConAccionesYSinSoloLectura()
    {
        var m = Montar(new IngresoCajaFake(), ConPermiso());

        Assert.True(Visible(Control<Button>(m, "BotonNuevo")));
        Assert.True(Visible(Control<Button>(m, "BotonEditar")));
        Assert.True(Visible(Control<Button>(m, "BotonBaja")));
        Assert.False(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
    }

    [AvaloniaFact]
    public void FilaInactivaSeleccionada_EditarYDarDeBajaDeshabilitados()
    {
        var m = Montar(Partida, Duplicado);
        m.Vm.MostrarInactivos = true;
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Fila(m, Duplicado));

        Assert.Same(Duplicado, m.Vm.ItemSeleccionado);
        Assert.False(Control<Button>(m, "BotonEditar").IsEffectivelyEnabled);
        Assert.False(Control<Button>(m, "BotonBaja").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnFilaActiva_ConPermiso_Edita()
    {
        var m = Montar(Partida);

        DobleClick(m.Ventana, CeldaConTexto(m, Partida.Concepto));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(typeof(IngresoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_OperadorMixto_NoEdita()
    {
        var m = Montar(new IngresoCajaFake { Lista = [Partida] }, Mixto());

        DobleClick(m.Ventana, CeldaConTexto(m, Partida.Concepto));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnFilaInactiva_NoEdita()
    {
        var m = Montar(Duplicado);
        m.Vm.MostrarInactivos = true;
        Dispatcher.UIThread.RunJobs();

        DobleClick(m.Ventana, CeldaConTexto(m, Duplicado.Concepto));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnElEncabezado_NoEdita()
    {
        var m = Montar(Partida);
        Clickear(m.Ventana, Fila(m, Partida));
        var encabezado = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Single(h => Equals(h.Content, "CONCEPTO"));

        DobleClick(m.Ventana, encabezado);
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void DarDeBaja_ConfirmaConConceptoYMonto_YDaDeBaja()
    {
        var m = Montar(Partida);
        Clickear(m.Ventana, Fila(m, Partida));

        Clickear(m.Ventana, Control<Button>(m, "BotonBaja"));

        var pregunta = Assert.Single(m.Confirmacion.Preguntas);
        Assert.Equal("¿Confirma dar de baja el ingreso \"Partida mensual FIGM octubre\" ($ 250.000,00)?", pregunta);
        Assert.Equal(new[] { 1 }, m.Servicio.Bajas);
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaRegistrarIngresos_OcultaAccionesSinNavegar()
    {
        var sesion = ConPermiso();
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new IngresosViewModel(new IngresoCajaFake { Lista = [Partida] }, sesion, new NavigationServiceFake(),
            new ConfirmacionServiceFake());
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
        var vista = ventana.GetVisualDescendants().OfType<IngresosView>().Single();
        Assert.True(Visible(MontajeV2.Parte<Button>(vista, "BotonNuevo")), "Precondición: con finanzas.ingresos se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonNuevo")));
        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonEditar")));
        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonBaja")));
        Assert.True(Visible(MontajeV2.Parte<Insignia>(vista, "InsigniaSoloLectura")));
        Assert.Same(vm, shell.CurrentContent);
    }
}
