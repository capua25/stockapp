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
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Listado de Documentos v2 con la View REAL: dos vistas (Activos e Historial) elegidas con el combo "Ver",
/// filtros que recargan solos, acciones del encabezado sobre la fila seleccionada según su estado y el rol (Operador de
/// permisos mixtos; Admin solo para las acciones estructurales), insignias, estados de pantalla, doble click, clicks reales
/// y revocación en caliente por el camino del shell.</summary>
public class DocumentoListViewV2Tests
{
    private static DocumentoAdministrativo D(int id, string numero, EstadoDocumento estado, TipoDocumento tipo = TipoDocumento.Expediente) => new()
    {
        Id = id, Numero = numero, Anio = 2026, Tipo = tipo, FechaEmision = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
        Descripcion = $"Descripción {numero}", Estado = estado, RegistradoPorUsuarioId = 1,
        RegistradoPor = new Usuario { Id = 1, NombreUsuario = "mperez" }, FechaRegistro = DateTime.UtcNow,
    };

    private static DocumentosFake Datos() => new()
    {
        Activos =
        [
            D(1, "0001", EstadoDocumento.Pendiente),
            D(2, "0002", EstadoDocumento.EnProceso, TipoDocumento.Oficio),
            D(3, "0003", EstadoDocumento.EnProceso, TipoDocumento.Suministro),
        ],
        Historial =
        [
            D(5, "0005", EstadoDocumento.Finalizado),
            D(6, "0006", EstadoDocumento.Anulado, TipoDocumento.Oficio),
        ],
    };

    private static SesionFake Admin() => new(RolUsuario.Admin);
    private static SesionFake Mixto() => new(RolUsuario.Operador, Permisos.GestionarDocumentos);

    private sealed record Montaje(Window Ventana, DocumentoListView Vista, DocumentoListViewModel Vm, DocumentosFake Servicio,
        NavigationRecorderFake Navegacion, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(DocumentosFake servicio, ICurrentSession? sesion = null)
    {
        var navegacion = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new DocumentoListViewModel(servicio, sesion ?? Admin(), navegacion, confirmacion) { EsperaDebounce = TimeSpan.Zero };
        var vista = new DocumentoListView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, navegacion, confirmacion);
    }

    private static Montaje Montar(ICurrentSession? sesion = null) => Montar(Datos(), sesion);

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static bool VisibleNombre(Montaje m, string nombre) => Visible(Control<Control>(m, nombre));

    private static void Esperar(Montaje m)
    {
        Dispatcher.UIThread.RunJobs();
        m.Vm._tareaDebounce.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static void ElegirHistorial(Montaje m)
    {
        m.Vm.VistaSeleccionada = m.Vm.Vistas[1];
        Esperar(m);
    }

    private static void SeleccionarActivo(Montaje m, int id)
    {
        m.Vm.FilaSeleccionadaActivos = m.Vm.Activos.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SeleccionarHistorial(Montaje m, int id)
    {
        m.Vm.FilaSeleccionadaHistorial = m.Vm.Historial.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridRow Fila(Montaje m, string grilla, int id) =>
        Control<DataGrid>(m, grilla).GetVisualDescendants().OfType<DataGridRow>()
            .Single(r => Visible(r) && r.DataContext is DocumentoFila f && f.Id == id);

    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

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

    // ── Estructura ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoDocumentos_NuevoDocumentoEsElUnicoPrimario()
    {
        var m = Montar();
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Documentos administrativos", encabezado.Titulo);
        Assert.Equal("Expedientes, oficios y suministros, y su estado.", encabezado.Subtitulo);
        SeleccionarActivo(m, 2);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b));
        Assert.Same(Control<Button>(m, "BotonNuevo"), Assert.Single(primarios));
        Assert.Same(m.Vm.NuevoCommand, Control<Button>(m, "BotonNuevo").Command);
        foreach (var nombre in new[] { "BotonVer", "BotonIniciar", "BotonVolverAPendiente", "BotonFinalizar", "BotonReabrir" })
            Assert.Contains("secundario", Control<Button>(m, nombre).Classes);
        Assert.Contains("peligro", Control<Button>(m, "BotonAnular").Classes);
    }

    [AvaloniaFact]
    public void Grillas_SeisColumnasEnOrden()
    {
        var m = Montar();
        string[] esperado = ["TIPO", "NÚMERO", "DESCRIPCIÓN", "EMISIÓN", "ESTADO", "REGISTRADO POR"];

        Assert.Equal(esperado, Control<DataGrid>(m, "GrillaActivos").Columns.Select(c => (string)c.Header).ToArray());
        Assert.Equal(esperado, Control<DataGrid>(m, "GrillaHistorial").Columns.Select(c => (string)c.Header).ToArray());
    }

    [AvaloniaFact]
    public void PorDefecto_ActivosVisiblesHistorialOcultoYSinConsultarlo()
    {
        var m = Montar();

        Assert.True(VisibleNombre(m, "GrillaActivos"), "GrillaActivos");
        Assert.True(VisibleNombre(m, "FiltrosActivos"), "FiltrosActivos");
        Assert.True(VisibleNombre(m, "PieActivos"), "PieActivos");
        Assert.Equal("3 documentos", Control<TextBlock>(m, "PieActivos").Text);
        Assert.False(VisibleNombre(m, "GrillaHistorial"));
        Assert.False(VisibleNombre(m, "FiltrosHistorial"));
        Assert.False(VisibleNombre(m, "PieHistorial"));
        Assert.Empty(m.Servicio.FiltrosHistorial);
    }

    [AvaloniaFact]
    public void ElegirHistorial_MuestraSusFiltrosYSuGrilla_SinHueco()
    {
        var m = Montar();
        var yGrillaActivos = EnVentana(m.Ventana, Control<DataGrid>(m, "GrillaActivos")).Y;

        ElegirHistorial(m);

        Assert.True(VisibleNombre(m, "FiltrosHistorial"));
        Assert.True(VisibleNombre(m, "CampoAnio"));
        Assert.True(VisibleNombre(m, "ComboEstadoHistorial"));
        Assert.True(VisibleNombre(m, "GrillaHistorial"));
        Assert.Equal("2 documentos", Control<TextBlock>(m, "PieHistorial").Text);
        Assert.False(VisibleNombre(m, "FiltrosActivos"));
        Assert.False(VisibleNombre(m, "GrillaActivos"));
        Assert.Single(m.Servicio.FiltrosHistorial);

        m.Vm.VistaSeleccionada = m.Vm.Vistas[0];
        Esperar(m);

        Assert.True(VisibleNombre(m, "GrillaActivos"));
        Assert.False(VisibleNombre(m, "FiltrosHistorial"));
        // El historial ya cargado (sus filas siguen en la colección) no se ve en la vista de Activos.
        Assert.NotEmpty(m.Vm.Historial);
        Assert.False(VisibleNombre(m, "GrillaHistorial"));
        Assert.False(VisibleNombre(m, "PieHistorial"));
        Assert.Equal(yGrillaActivos, EnVentana(m.Ventana, Control<DataGrid>(m, "GrillaActivos")).Y, 0.5);
    }

    [AvaloniaFact]
    public void Historial_AnioInvalido_ErrorDebajoYSinConsulta()
    {
        var m = Montar();
        ElegirHistorial(m);
        var anio = Control<NumericUpDown>(m, "SelectorAnio");
        var consultas = m.Servicio.FiltrosHistorial.Count;

        NumericUpDownPrueba.Tipear(anio, "20a5");
        Esperar(m);

        NumericUpDownPrueba.AssertErrorVisible(anio, "Ingresá un año válido, entre 2000 y 2100.");
        Assert.True(m.Vm.HayErroresDeEntrada);
        Assert.Equal(consultas, m.Servicio.FiltrosHistorial.Count);
    }

    [AvaloniaFact]
    public void CambiarElTipoEnActivos_RecargaSola_ConElFiltroEnLaConsulta()
    {
        var m = Montar();

        Control<ComboBox>(m, "ComboTipoActivos").SelectedItem = m.Vm.TiposDisponibles.Single(o => o.Valor == TipoDocumento.Oficio);
        Esperar(m);

        Assert.Equal(2, m.Servicio.FiltrosActivos.Count);
        Assert.Equal(TipoDocumento.Oficio, m.Servicio.FiltrosActivos[^1].Tipo);
    }

    // ── Acciones de fila ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SeleccionEnProceso_ComoAdmin_VerVolverAPendienteFinalizarYAnular()
    {
        var m = Montar(Admin());

        SeleccionarActivo(m, 2);

        Assert.True(Control<Button>(m, "BotonVer").IsEnabled);
        Assert.True(VisibleNombre(m, "BotonVolverAPendiente"));
        Assert.True(VisibleNombre(m, "BotonFinalizar"));
        Assert.True(VisibleNombre(m, "BotonAnular"));
        Assert.False(VisibleNombre(m, "BotonIniciar"));
        Assert.False(VisibleNombre(m, "BotonReabrir"));
    }

    [AvaloniaFact]
    public void SeleccionEnProceso_ComoOperadorMixto_SinAnular()
    {
        var m = Montar(Mixto());

        SeleccionarActivo(m, 2);

        Assert.True(VisibleNombre(m, "BotonVolverAPendiente"));
        Assert.True(VisibleNombre(m, "BotonFinalizar"));
        Assert.False(VisibleNombre(m, "BotonAnular"));
    }

    [AvaloniaFact]
    public void SeleccionPendiente_ComoOperadorMixto_IniciarVisible()
    {
        var m = Montar(Mixto());

        SeleccionarActivo(m, 1);

        Assert.True(VisibleNombre(m, "BotonIniciar"));
        Assert.False(VisibleNombre(m, "BotonFinalizar"));
        Assert.False(VisibleNombre(m, "BotonVolverAPendiente"));
    }

    [AvaloniaFact]
    public void SeleccionFinalizado_EnHistorial_ComoAdmin_VerYReabrir()
    {
        var m = Montar(Admin());
        ElegirHistorial(m);

        SeleccionarHistorial(m, 5);

        Assert.True(Control<Button>(m, "BotonVer").IsEnabled);
        Assert.True(VisibleNombre(m, "BotonReabrir"));
        Assert.False(VisibleNombre(m, "BotonAnular"));
        Assert.False(VisibleNombre(m, "BotonFinalizar"));
    }

    [AvaloniaFact]
    public void SeleccionFinalizado_ComoOperadorMixto_SinReabrir()
    {
        var m = Montar(Mixto());
        ElegirHistorial(m);

        SeleccionarHistorial(m, 5);

        Assert.True(Control<Button>(m, "BotonVer").IsEnabled);
        Assert.False(VisibleNombre(m, "BotonReabrir"));
    }

    [AvaloniaFact]
    public void SinSeleccion_AccionesDeFilaOcultasYVerDeshabilitado()
    {
        var m = Montar(Admin());

        Assert.False(Control<Button>(m, "BotonVer").IsEnabled);
        foreach (var nombre in new[] { "BotonIniciar", "BotonVolverAPendiente", "BotonFinalizar", "BotonAnular", "BotonReabrir" })
            Assert.False(VisibleNombre(m, nombre), nombre);
        Assert.True(VisibleNombre(m, "BotonNuevo"));
    }

    [AvaloniaFact]
    public void AccionesSiguenALaGrillaVisible()
    {
        var m = Montar(Admin());
        SeleccionarActivo(m, 2);
        Assert.True(VisibleNombre(m, "BotonFinalizar"));

        ElegirHistorial(m);

        Assert.Null(m.Vm.FilaSeleccionada);
        Assert.False(Control<Button>(m, "BotonVer").IsEnabled);
        Assert.False(VisibleNombre(m, "BotonFinalizar"));
        Assert.False(VisibleNombre(m, "BotonVolverAPendiente"));
        Assert.False(VisibleNombre(m, "BotonAnular"));
    }

    [AvaloniaFact]
    public void DobleClickEnUnaFila_NavegaAlDetalle()
    {
        var m = Montar();

        DobleClick(m.Ventana, Fila(m, "GrillaActivos", 2));

        Assert.Equal(typeof(DocumentoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    // ── Insignias y fila anulada ────────────────────────────────────────────────

    [AvaloniaFact]
    public void FilaAnulada_TextoInactivoEInsigniaNeutra()
    {
        var m = Montar();
        ElegirHistorial(m);

        var fila = Fila(m, "GrillaHistorial", 6);
        var textos = fila.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null && Visible(t)).ToList();
        Assert.NotEmpty(textos);
        Assert.All(textos, t => Assert.Contains("inactivo", t.Classes));
        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Anulado");
        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);
        var finalizada = Fila(m, "GrillaHistorial", 5).GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Finalizado");
        Assert.Equal(VarianteInsignia.Exito, finalizada.Variante);
    }

    [AvaloniaFact]
    public void EstadoInsignia_EnProcesoConTextoLegible()
    {
        var m = Montar();

        var insignia = Fila(m, "GrillaActivos", 2).GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "En proceso");
        Assert.Equal(VarianteInsignia.Advertencia, insignia.Variante);
        Assert.DoesNotContain(Control<DataGrid>(m, "GrillaActivos").GetVisualDescendants().OfType<Insignia>(), i => i.Texto == "EnProceso");
    }

    // ── Estados de pantalla ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Cargando_IndicadorSinGrillaNiPie()
    {
        var servicio = Datos();
        servicio.Pendiente = new TaskCompletionSource<IReadOnlyList<DocumentoAdministrativo>>();
        var m = Montar(servicio);

        Assert.True(VisibleNombre(m, "IndicadorCarga"));
        Assert.False(VisibleNombre(m, "GrillaActivos"));
        Assert.False(VisibleNombre(m, "PieActivos"));
        Assert.True(VisibleNombre(m, "FiltrosActivos"));
    }

    [AvaloniaFact]
    public void SinConexion_EstadoConReintentarYFiltrosAMano()
    {
        var servicio = Datos();
        servicio.Falla = new ServidorNoDisponibleException();
        var m = Montar(servicio);

        Assert.True(VisibleNombre(m, "EstadoSinConexion"));
        Assert.True(VisibleNombre(m, "BotonReintentar"));
        Assert.True(VisibleNombre(m, "FiltrosActivos"));
        Assert.False(VisibleNombre(m, "GrillaActivos"));
        Assert.False(VisibleNombre(m, "IndicadorCarga"));
        Assert.Empty(m.Confirmacion.MensajesInformados);
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard()
    {
        var servicio = Datos();
        servicio.Falla = new UnauthorizedAccessException();
        var m = Montar(servicio);

        Assert.True(VisibleNombre(m, "EstadoSinPermiso"));
        Assert.False(VisibleNombre(m, "Tarjeta"));
        Assert.Equal(DocumentoListViewModel.MensajeSinPermisoDocumentos, Control<EstadoVacio>(m, "EstadoSinPermiso").Mensaje);
        Assert.Empty(m.Confirmacion.MensajesInformados);
    }

    [AvaloniaFact]
    public void SinDocumentos_VacioConNuevoDocumento()
    {
        var m = Montar(new DocumentosFake());

        Assert.True(VisibleNombre(m, "EstadoSinDocumentos"));
        Assert.False(VisibleNombre(m, "EstadoSinResultados"));
        Assert.Contains("secundario", Control<Button>(m, "BotonNuevoVacio").Classes);
        Assert.DoesNotContain("primario", Control<Button>(m, "BotonNuevoVacio").Classes);
    }

    [AvaloniaFact]
    public void ConFiltros_SinResultados()
    {
        var m = Montar(new DocumentosFake());
        Control<TextBox>(m, "CajaBuscarActivos").Text = "licitación";
        Esperar(m);

        Assert.True(VisibleNombre(m, "EstadoSinResultados"));
        Assert.False(VisibleNombre(m, "EstadoSinDocumentos"));
    }

    [AvaloniaFact]
    public void HistorialSinFilas_MuestraSinResultados_NoElVacioDeActivos()
    {
        var servicio = Datos();
        servicio.Historial.Clear();
        var m = Montar(servicio);

        ElegirHistorial(m);

        Assert.True(VisibleNombre(m, "EstadoSinResultados"));
        Assert.False(VisibleNombre(m, "EstadoSinDocumentos"));
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    [AvaloniaFact]
    public async Task RevocacionDentroDelShell_SinPermisoEnLugarDeLaCard()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.GestionarDocumentos);
        var tras = new HashSet<string>();
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new DocumentoListViewModel(Datos(), sesion, new NavigationRecorderFake(), new ConfirmacionServiceFake()) { EsperaDebounce = TimeSpan.Zero };
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
        T EnDocumentos<T>(string nombre) where T : Control =>
            MontajeV2.Parte<T>(ventana.GetVisualDescendants().OfType<DocumentoListView>().Single(), nombre);
        Assert.True(ArbolVisual.EsVisibleEnArbol(EnDocumentos<TarjetaContenido>("Tarjeta")), "Precondición: con documentos.gestionar se ve la card.");
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnDocumentos<EstadoVacio>("EstadoSinPermiso")), "Precondición: con el permiso no hay Sin permiso.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ArbolVisual.EsVisibleEnArbol(EnDocumentos<EstadoVacio>("EstadoSinPermiso")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnDocumentos<TarjetaContenido>("Tarjeta")));
    }

    // ── Clicks reales ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ClickReal_EnFinalizar_LlamaAlServicioYRecarga()
    {
        var m = Montar(Mixto());
        SeleccionarActivo(m, 2);
        var antes = m.Servicio.FiltrosActivos.Count;

        Clickear(m.Ventana, Control<Button>(m, "BotonFinalizar"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([2], m.Servicio.Finalizados);
        Assert.Equal(antes + 1, m.Servicio.FiltrosActivos.Count);
        Assert.DoesNotContain(m.Vm.Activos, f => f.Id == 2);
    }

    [AvaloniaFact]
    public void ClickReal_EnAnular_NavegaAlDetalle()
    {
        var m = Montar(Admin());
        SeleccionarActivo(m, 1);

        Clickear(m.Ventana, Control<Button>(m, "BotonAnular"));

        Assert.Equal(typeof(DocumentoFormViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Anulados);
    }
}
