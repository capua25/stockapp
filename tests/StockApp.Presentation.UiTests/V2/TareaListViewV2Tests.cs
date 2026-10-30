using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Listado de Tareas v2 con la View REAL: estructura, columnas, filtro de Estado, acciones de fila según la
/// selección y el rol (Operador de permisos mixtos, nunca Admin para un gate de permiso), insignias, estados de pantalla,
/// doble click, clicks reales y revocación en caliente por el camino del shell.</summary>
public class TareaListViewV2Tests
{
    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static Tarea T(int id, string titulo, EstadoTarea estado, DateTime? limite = null, string? tomada = null) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media, CreadaPorUsuarioId = 1,
        FechaCreacion = new DateTime(2026, 10, id, 8, 0, 0, DateTimeKind.Utc), FechaLimite = limite,
        TomadaPor = tomada is null ? null : new Usuario { Id = 5, NombreUsuario = tomada }, TomadaPorUsuarioId = tomada is null ? null : 5,
    };

    private static List<Tarea> Datos() =>
    [
        T(1, "Relevar luminarias", EstadoTarea.Pendiente, Dia(2030, 3, 15)),
        T(2, "Reparar bache", EstadoTarea.Pendiente, Dia(2026, 9, 30)),
        T(3, "Poda de plátanos", EstadoTarea.EnCurso, Dia(2030, 2, 1), "mperez"),
        T(4, "Limpieza de cunetas", EstadoTarea.EnCurso, Dia(2030, 5, 20), "jsosa"),
        T(5, "Pintura de cordones", EstadoTarea.Terminada, Dia(2030, 1, 10), "mperez"),
        T(6, "Bancos en la peatonal", EstadoTarea.Cancelada, Dia(2030, 4, 4)),
    ];

    private static SesionFake Admin() => new(RolUsuario.Admin);
    private static SesionFake Mixto() => new(RolUsuario.Operador, Permisos.GestionarTareas);

    private sealed record Montaje(Window Ventana, TareaListView Vista, TareaListViewModel Vm, TareasFake Servicio,
        NavigationRecorderFake Navegacion, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(TareasFake servicio, ICurrentSession? sesion = null, double ancho = 1040)
    {
        var navegacion = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new TareaListViewModel(servicio, sesion ?? Admin(), navegacion, confirmacion);
        var vista = new TareaListView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, navegacion, confirmacion);
    }

    private static Montaje Montar(ICurrentSession? sesion = null) => Montar(new TareasFake { Lista = Datos() }, sesion);

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static bool VisibleNombre(Montaje m, string nombre) => Visible(Control<Control>(m, nombre));

    private static void Seleccionar(Montaje m, int id)
    {
        m.Vm.FilaSeleccionada = m.Vm.Tareas.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridRow Fila(Montaje m, int id) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridRow>()
            .Single(r => Visible(r) && r.DataContext is TareaFila f && f.Id == id);

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

    private static async Task Esperar(Func<bool> condicion)
    {
        for (var i = 0; i < 100 && !condicion(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    // ── Estructura ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoTareas_NuevaTareaEsElUnicoPrimario()
    {
        var m = Montar();
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Tareas", encabezado.Titulo);
        Assert.Equal("Trabajos de la intendencia: quién los toma y cuándo vencen.", encabezado.Subtitulo);
        Seleccionar(m, 3);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b));
        Assert.Same(Control<Button>(m, "BotonNueva"), Assert.Single(primarios));
        Assert.Same(m.Vm.NuevaCommand, Control<Button>(m, "BotonNueva").Command);
        foreach (var nombre in new[] { "BotonVer", "BotonTomar", "BotonSoltar", "BotonTerminar" })
            Assert.Contains("secundario", Control<Button>(m, nombre).Classes);
        Assert.Contains("peligro", Control<Button>(m, "BotonCancelarTarea").Classes);
    }

    [AvaloniaFact]
    public void Grilla_SeisColumnasEnOrden()
    {
        var m = Montar();

        Assert.Equal(["TÍTULO", "ESTADO", "PRIORIDAD", "VENCE", "TOMADA POR", "PROYECTO"],
            Control<DataGrid>(m, "Grilla").Columns.Select(c => (string)c.Header).ToArray());
    }

    [AvaloniaFact]
    public void FiltroEstado_PorDefectoAbiertas_ConteoYFilas()
    {
        var m = Montar();

        var combo = Control<ComboBox>(m, "ComboEstado");
        Assert.Equal("Abiertas", ((OpcionEstadoTarea)combo.SelectedItem!).Nombre);
        var filas = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridRow>().Where(r => Visible(r)).ToList();
        Assert.Equal(4, filas.Count);
        Assert.Equal("4 tareas", Control<TextBlock>(m, "PieConteo").Text);
        Assert.True(VisibleNombre(m, "PieConteo"));
        Assert.False(VisibleNombre(m, "EstadoSinPermiso"), "Sin 403, Sin permiso no se muestra.");
        Assert.True(VisibleNombre(m, "Tarjeta"));
    }

    // ── Acciones de fila ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SinSeleccion_AccionesDeFilaOcultasYVerDeshabilitado()
    {
        var m = Montar();

        Assert.False(Control<Button>(m, "BotonVer").IsEnabled);
        foreach (var nombre in new[] { "BotonTomar", "BotonSoltar", "BotonTerminar", "BotonCancelarTarea" })
            Assert.False(VisibleNombre(m, nombre), nombre);
        Assert.True(VisibleNombre(m, "BotonNueva"));
    }

    [AvaloniaFact]
    public void SeleccionEnCurso_ComoAdmin_VerSoltarTerminarYCancelarTarea()
    {
        var m = Montar(Admin());

        Seleccionar(m, 3);

        Assert.True(Control<Button>(m, "BotonVer").IsEnabled);
        Assert.True(VisibleNombre(m, "BotonSoltar"));
        Assert.True(VisibleNombre(m, "BotonTerminar"));
        Assert.True(VisibleNombre(m, "BotonCancelarTarea"));
        Assert.False(VisibleNombre(m, "BotonTomar"));
    }

    [AvaloniaFact]
    public void SeleccionPendiente_ComoOperadorMixto_VerYTomarSinCancelarTarea()
    {
        var m = Montar(Mixto());

        Seleccionar(m, 1);

        Assert.True(Control<Button>(m, "BotonVer").IsEnabled);
        Assert.True(VisibleNombre(m, "BotonTomar"));
        Assert.False(VisibleNombre(m, "BotonCancelarTarea"));
        Assert.False(VisibleNombre(m, "BotonSoltar"));
        Assert.False(VisibleNombre(m, "BotonTerminar"));
    }

    [AvaloniaFact]
    public void FiltroQueOcultaLaSeleccion_OcultaLasAccionesDeFila()
    {
        var m = Montar(Admin());
        Seleccionar(m, 3);
        Assert.True(VisibleNombre(m, "BotonTerminar"));

        m.Vm.FiltroEstado = m.Vm.FiltrosEstado.Single(o => o.Nombre == "Pendientes");
        Dispatcher.UIThread.RunJobs();

        Assert.Null(m.Vm.FilaSeleccionada);
        foreach (var nombre in new[] { "BotonSoltar", "BotonTerminar", "BotonCancelarTarea", "BotonTomar" })
            Assert.False(VisibleNombre(m, nombre), nombre);
        Assert.False(Control<Button>(m, "BotonVer").IsEnabled);
    }

    // ── Doble click ─────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DobleClickEnUnaFila_NavegaAlDetalle()
    {
        var m = Montar();

        DobleClick(m.Ventana, Fila(m, 3));

        Assert.Equal(typeof(TareaFormViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.NotNull(m.Navegacion.UltimoInicializadorTareaForm);
    }

    [AvaloniaFact]
    public void DobleClickEnElEncabezado_NoNavega()
    {
        var m = Montar();
        var header = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Visible(h));

        DobleClick(m.Ventana, header);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    // ── Insignias y fila cancelada ──────────────────────────────────────────────

    private static IEnumerable<Insignia> InsigniasDe(Control fila) => fila.GetVisualDescendants().OfType<Insignia>();

    [AvaloniaFact]
    public void FilaCancelada_TextoInactivoEInsigniaNeutra()
    {
        var m = Montar();
        m.Vm.FiltroEstado = m.Vm.FiltrosEstado.Single(o => o.Nombre == "Canceladas");
        Dispatcher.UIThread.RunJobs();

        var fila = Fila(m, 6);
        var textos = fila.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null && Visible(t)).ToList();
        Assert.NotEmpty(textos);
        Assert.All(textos, t => Assert.Contains("inactivo", t.Classes));
        var insignia = InsigniasDe(fila).Single(i => i.Texto == "Cancelada");
        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);
    }

    [AvaloniaFact]
    public void FilaVencida_InsigniaVencidaPeligroEnVence()
    {
        var m = Montar();

        var vencidas = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<Insignia>().Where(i => i.Texto == "Vencida" && Visible(i)).ToList();
        var unica = Assert.Single(vencidas);
        Assert.Equal(VarianteInsignia.Peligro, unica.Variante);
        Assert.Equal(2, ((TareaFila)unica.FindAncestorOfType<DataGridRow>()!.DataContext!).Id);
    }

    [AvaloniaFact]
    public void EstadoInsignia_ConTextoLegible()
    {
        var m = Montar();

        var insignia = InsigniasDe(Fila(m, 3)).Single(i => i.Texto == "En curso");
        Assert.Equal(VarianteInsignia.Advertencia, insignia.Variante);
        Assert.DoesNotContain(Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<Insignia>(), i => i.Texto == "EnCurso");
    }

    // ── Estados de pantalla ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Cargando_IndicadorSinGrillaNiPie()
    {
        var servicio = new TareasFake();
        servicio.ListarPendiente();
        var m = Montar(servicio);

        Assert.True(VisibleNombre(m, "IndicadorCarga"));
        Assert.False(VisibleNombre(m, "Grilla"));
        Assert.False(VisibleNombre(m, "PieConteo"));
        Assert.True(VisibleNombre(m, "Filtros"));
        Assert.False(VisibleNombre(m, "EstadoSinTareas"), "Cargando no es 'no hay tareas'.");
        Assert.False(VisibleNombre(m, "EstadoSinResultados"));
    }

    [AvaloniaFact]
    public void Recargando_ConElFiltroSinResultados_NoMuestraElVacioDelFiltro()
    {
        var m = Montar();
        m.Vm.FiltroEstado = m.Vm.FiltrosEstado.Single(o => o.Nombre == "Canceladas");
        m.Servicio.Lista.RemoveAll(t => t.Estado == EstadoTarea.Cancelada);
        _ = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(VisibleNombre(m, "EstadoSinResultados"), "Precondición: con el filtro y sin cancelada se ve el vacío del filtro.");

        m.Servicio.ListarPendiente();
        _ = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.Cargando);
        Assert.False(VisibleNombre(m, "EstadoSinResultados"), "Mientras recarga no se afirma que no hay resultados.");
    }

    [AvaloniaFact]
    public void SinConexion_EstadoConReintentarYFiltroAMano()
    {
        var m = Montar(new TareasFake { Falla = new ServidorNoDisponibleException() });

        Assert.True(VisibleNombre(m, "EstadoSinConexion"));
        Assert.True(VisibleNombre(m, "BotonReintentar"));
        Assert.True(VisibleNombre(m, "Filtros"));
        Assert.False(VisibleNombre(m, "Grilla"));
        Assert.False(VisibleNombre(m, "IndicadorCarga"));
        Assert.Empty(m.Confirmacion.MensajesInformados);
        Assert.False(VisibleNombre(m, "EstadoSinTareas"), "Sin conexión no es 'no hay tareas'.");
        Assert.False(VisibleNombre(m, "EstadoSinResultados"));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard()
    {
        var m = Montar(new TareasFake { Falla = new UnauthorizedAccessException() });

        Assert.True(VisibleNombre(m, "EstadoSinPermiso"));
        Assert.False(VisibleNombre(m, "Tarjeta"));
        Assert.Equal(TareaListViewModel.MensajeSinPermisoTareas, Control<EstadoVacio>(m, "EstadoSinPermiso").Mensaje);
        Assert.Empty(m.Confirmacion.MensajesInformados);
    }

    [AvaloniaFact]
    public void SinTareas_VacioConNuevaTareaSecundario()
    {
        var m = Montar(new TareasFake());

        Assert.True(VisibleNombre(m, "EstadoSinTareas"));
        Assert.False(VisibleNombre(m, "EstadoSinResultados"));
        Assert.Contains("secundario", Control<Button>(m, "BotonNuevaVacio").Classes);
        Assert.DoesNotContain("primario", Control<Button>(m, "BotonNuevaVacio").Classes);
    }

    [AvaloniaFact]
    public void SinResultados_VacioDelFiltro()
    {
        var m = Montar();
        m.Vm.FiltroEstado = m.Vm.FiltrosEstado.Single(o => o.Nombre == "Canceladas");
        m.Servicio.Lista.RemoveAll(t => t.Estado == EstadoTarea.Cancelada);
        _ = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(VisibleNombre(m, "EstadoSinResultados"));
        Assert.False(VisibleNombre(m, "EstadoSinTareas"));
        Assert.False(VisibleNombre(m, "Grilla"));
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    [AvaloniaFact]
    public async Task RevocacionDentroDelShell_SinPermisoEnLugarDeLaCard()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.GestionarTareas);
        var tras = new HashSet<string>();
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new TareaListViewModel(new TareasFake { Lista = Datos() }, sesion, new NavigationRecorderFake(), new ConfirmacionServiceFake());
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
        T EnTareas<T>(string nombre) where T : Control =>
            MontajeV2.Parte<T>(ventana.GetVisualDescendants().OfType<TareaListView>().Single(), nombre);
        Assert.True(ArbolVisual.EsVisibleEnArbol(EnTareas<TarjetaContenido>("Tarjeta")), "Precondición: con tareas.gestionar se ve la card.");
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnTareas<EstadoVacio>("EstadoSinPermiso")), "Precondición: con el permiso no hay Sin permiso.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ArbolVisual.EsVisibleEnArbol(EnTareas<EstadoVacio>("EstadoSinPermiso")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnTareas<TarjetaContenido>("Tarjeta")));
    }

    // ── Clicks reales ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ClickReal_EnTomar_LlamaAlServicioYRecarga()
    {
        var m = Montar(Mixto());
        Seleccionar(m, 1);
        var antes = m.Servicio.LlamadasListar;

        Clickear(m.Ventana, Control<Button>(m, "BotonTomar"));
        await Esperar(() => m.Servicio.LlamadasListar > antes);

        Assert.Equal([1], m.Servicio.Tomadas);
        Assert.Equal(antes + 1, m.Servicio.LlamadasListar);
        Assert.Equal(EstadoTarea.EnCurso, m.Servicio.Lista.Single(t => t.Id == 1).Estado);
    }

    [AvaloniaFact]
    public async Task ClickReal_EnCancelarTarea_PideConfirmacionConElTextoDeSiempre()
    {
        var m = Montar(Admin());
        Seleccionar(m, 3);

        Clickear(m.Ventana, Control<Button>(m, "BotonCancelarTarea"));
        await Esperar(() => m.Servicio.Canceladas.Count > 0);

        Assert.Equal("¿Confirma cancelar la tarea \"Poda de plátanos\"?", Assert.Single(m.Confirmacion.Preguntas));
        Assert.Equal([3], m.Servicio.Canceladas);
    }
}
