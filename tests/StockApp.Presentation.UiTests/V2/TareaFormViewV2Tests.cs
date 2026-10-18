using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de Tareas v2 con la View REAL: alta (breadcrumb, panel de clasificación en dos columnas, error de
/// Título por campo, error del servidor) y detalle (solo lectura, gates de rol, insignia, hora local de las notas), con el
/// pie fijo fuera del scroll a los dos tamaños.</summary>
public class TareaFormViewV2Tests
{
    private static readonly Zona Centro = new() { Id = 1, Nombre = "Centro", Activo = true };

    private sealed record Montaje(Window Ventana, TareaFormView Vista, TareaFormViewModel Vm, TareasFake Servicio,
        DocumentosFake Documentos, NavigationRecorderFake Navegacion);

    private static Montaje Montar(Action<TareaFormViewModel>? preparar = null, ICurrentSession? sesion = null, TareasFake? servicio = null,
        DocumentosFake? documentos = null, double ancho = 1040, double alto = 760)
    {
        servicio ??= new TareasFake();
        documentos ??= new DocumentosFake();
        sesion ??= new SesionFake(RolUsuario.Admin);
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new ClasificacionTareaPanelViewModel(new ZonaServiceFake([Centro]), new DimensionTematicaServiceFake(),
            new OrganismoResponsableServiceFake(), new OrigenFinanciamientoServiceFake(), documentos, confirmacion);
        var navegacion = new NavigationRecorderFake();
        var vm = new TareaFormViewModel(servicio, sesion, navegacion, confirmacion, panel, new ClasificacionDialogServiceFake());
        preparar?.Invoke(vm);
        var vista = new TareaFormView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.DataTemplates.Add(new ViewLocator()); // el ContentControl del panel resuelve su View por el ViewLocator de la app
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, documentos, navegacion);
    }

    private static Montaje Alta(ICurrentSession? sesion = null, TareasFake? servicio = null, DocumentosFake? documentos = null) =>
        Montar(vm => vm.CargarParaCrear(), sesion, servicio, documentos);

    private static Tarea Existente(EstadoTarea estado = EstadoTarea.EnCurso) => new()
    {
        Id = 7, Titulo = "Poda de plátanos", Descripcion = "Podar los secos.", Estado = estado, Prioridad = PrioridadTarea.Alta,
        CreadaPorUsuarioId = 1, FechaCreacion = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        FechaLimite = new DateTime(2030, 2, 1, 0, 0, 0, DateTimeKind.Utc), TomadaPor = new Usuario { Id = 2, NombreUsuario = "mperez" },
        Notas = [new NotaTarea { Id = 1, TareaId = 7, Texto = "Se podó el lado norte.", Fecha = new DateTime(2026, 10, 15, 13, 0, 0, DateTimeKind.Utc) }],
    };

    private static Montaje Detalle(ICurrentSession? sesion = null, Tarea? tarea = null, double ancho = 1040, double alto = 760)
    {
        var servicio = new TareasFake { Lista = [tarea ?? Existente()] };
        return Montar(vm => vm.CargarParaVer(servicio.Lista[0]), sesion, servicio, ancho: ancho, alto: alto);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static bool VisibleNombre(Montaje m, string nombre) => Visible(Parte<Control>(m, nombre));
    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);
    private static IEnumerable<string> Errores(Control c) => DataValidationErrors.GetErrors(c)?.Select(e => e?.ToString() ?? "") ?? [];

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<Button> PrimariosVisibles(Montaje m) =>
        m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b));

    // ── Alta ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Alta_BreadcrumbTareasNuevaTarea_GuardarEsElUnicoPrimario()
    {
        var m = Alta();
        var encabezado = Parte<EncabezadoPagina>(m, "EncabezadoAlta");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.True(Visible(encabezado));
        Assert.False(VisibleNombre(m, "EncabezadoDetalle"));
        Assert.Equal("Tareas", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nueva tarea", encabezado.BreadcrumbActual);
        Assert.Equal("Nueva tarea", encabezado.Titulo);
        Assert.Same(m.Vm.VolverCommand, encabezado.BreadcrumbComando);
        Assert.Same(Parte<Button>(m, "BotonGuardar"), Assert.Single(PrimariosVisibles(m)));
        Assert.Contains("secundario", Parte<Button>(m, "BotonVolver").Classes);
    }

    [AvaloniaFact]
    public void Alta_SeccionesDatosYClasificacion_PanelEnDosColumnas()
    {
        var m = Alta();
        var zona = EnVentana(m.Ventana, Parte<ComboBox>(m, "ComboZona"));
        var dimension = EnVentana(m.Ventana, Parte<ComboBox>(m, "ComboDimension"));
        var organismo = EnVentana(m.Ventana, Parte<ComboBox>(m, "ComboOrganismo"));
        var origen = EnVentana(m.Ventana, Parte<ComboBox>(m, "ComboOrigen"));
        var expediente = EnVentana(m.Ventana, Parte<AutoCompleteBox>(m, "BuscadorExpediente"));

        Assert.True(VisibleNombre(m, "SeccionClasificacionAlta"));
        Assert.Equal(zona.Y, dimension.Y, 0.5);
        Assert.Equal(organismo.Y, origen.Y, 0.5);
        Assert.True(dimension.X > zona.Right, "Dimensión no está a la derecha de Zona.");
        Assert.True(organismo.Y > zona.Bottom);
        Assert.True(expediente.Y > organismo.Bottom);
        Assert.Equal(origen.Right - zona.X, expediente.Width, 1);
        Assert.False(VisibleNombre(m, "SeccionDetalle"));
    }

    [AvaloniaFact]
    public void Alta_TocarTituloYSalir_ElErrorDebajoDeLaCaja()
    {
        var m = Alta();
        Parte<TextBox>(m, "CajaTitulo").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaTitulo")));

        Parte<TextBox>(m, "CajaDescripcion").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([TareaFormViewModel.MensajeTituloObligatorio], Errores(Parte<TextBox>(m, "CajaTitulo")));
        Assert.True(DataValidationErrors.GetHasErrors(Parte<TextBox>(m, "CajaTitulo")), "GetHasErrors");
        var texto = Parte<TarjetaContenido>(m, "Tarjeta").GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == TareaFormViewModel.MensajeTituloObligatorio && Visible(t));
        // El TextBox crece hacia abajo con el mensaje: el texto va debajo de los 36 de la caja.
        Assert.True(EnVentana(m.Ventana, texto).Y >= EnVentana(m.Ventana, Parte<TextBox>(m, "CajaTitulo")).Y + 36 - 1);
    }

    [AvaloniaFact]
    public void Alta_ErrorDelServidor_AlertaArribaDelPie()
    {
        var servicio = new TareasFake { FallaAlCrear = new ReglaDeNegocioException("No se pudo crear la tarea.") };
        var m = Alta(servicio: servicio);
        m.Vm.Titulo = "Reparar bache";
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("No se pudo crear la tarea.", alerta.Texto);
        Assert.False(Parte<ScrollViewer>(m, "Scroll").IsVisualAncestorOf(alerta));
        Assert.True(EnVentana(m.Ventana, alerta).Bottom <= EnVentana(m.Ventana, Parte<Border>(m, "Pie")).Y + 0.5);
    }

    [AvaloniaFact]
    public async Task BuscadorExpediente_OperadorSinDocumentos_MuestraElMensaje()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.GestionarTareas);
        var m = Alta(sesion, documentos: new DocumentosFake { Falla = new UnauthorizedAccessException() });
        Assert.False(VisibleNombre(m, "MensajeExpediente"));

        await m.Vm.ClasificacionPanel.BuscarExpedientesAsync("exp", CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(VisibleNombre(m, "MensajeExpediente"));
        Assert.Equal("No tenés permiso para buscar expedientes.", Parte<TextBlock>(m, "MensajeExpediente").Text);
    }

    // ── Detalle ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Detalle_SinPrimario()
    {
        var m = Detalle();

        Assert.True(VisibleNombre(m, "EncabezadoDetalle"));
        Assert.False(VisibleNombre(m, "EncabezadoAlta"));
        Assert.Equal("Detalle de la tarea", Parte<EncabezadoPagina>(m, "EncabezadoDetalle").BreadcrumbActual);
        Assert.Empty(PrimariosVisibles(m));
        Assert.False(VisibleNombre(m, "BotonGuardar"));
        Assert.True(VisibleNombre(m, "BotonVolver"));
    }

    [AvaloniaFact]
    public void Detalle_Admin_ReclasificarYPrioridadVisibles()
    {
        var m = Detalle(new SesionFake(RolUsuario.Admin));

        Assert.True(VisibleNombre(m, "BotonReclasificar"));
        Assert.True(VisibleNombre(m, "SeccionPrioridad"));
        Assert.True(VisibleNombre(m, "ComboPrioridad"));
        Assert.True(VisibleNombre(m, "BotonActualizarPrioridad"));
    }

    [AvaloniaFact]
    public void Detalle_OperadorMixto_SinReclasificarNiPrioridad()
    {
        var m = Detalle(new SesionFake(RolUsuario.Operador, Permisos.GestionarTareas));

        Assert.False(VisibleNombre(m, "BotonReclasificar"));
        Assert.False(VisibleNombre(m, "SeccionPrioridad"));
        Assert.True(VisibleNombre(m, "SeccionNotas"));
    }

    [AvaloniaFact]
    public void Detalle_AdminConTareaTerminada_ReclasificarSinPrioridad()
    {
        var m = Detalle(new SesionFake(RolUsuario.Admin), Existente(EstadoTarea.Terminada));

        Assert.True(VisibleNombre(m, "BotonReclasificar"));
        Assert.False(VisibleNombre(m, "SeccionPrioridad"));
    }

    [AvaloniaFact]
    public void Detalle_EstadoComoInsigniaConTextoLegible()
    {
        var m = Detalle();

        var insignia = Parte<Insignia>(m, "InsigniaEstado");
        Assert.Equal("En curso", insignia.Texto);
        Assert.Equal(VarianteInsignia.Advertencia, insignia.Variante);
    }

    [AvaloniaFact]
    public void Detalle_ClasificacionSinValor_MuestraGuion()
    {
        var m = Detalle();

        foreach (var nombre in new[] { "ValorZona", "ValorDimension", "ValorOrganismo", "ValorOrigen", "ValorExpediente" })
            Assert.Equal("—", Parte<TextBlock>(m, nombre).Text);
    }

    /// <summary>Una nota de las 13:00 UTC se lee 10:00 (Uruguay, UTC-3, sin horario de verano): la zona se fija para que el
    /// resultado no dependa de la máquina que corre la suite.</summary>
    [AvaloniaFact]
    public void Nota_FechaEnHoraLocal()
    {
        var zonaAntes = FechaHoraLocalConverter.Zona;
        FechaHoraLocalConverter.Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");
        try
        {
            var m = Detalle();
            Assert.Equal(new DateTime(2026, 10, 15, 13, 0, 0, DateTimeKind.Utc), m.Vm.Notas.Single().Fecha);

            var textos = Parte<ItemsControl>(m, "ListaNotas").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

            Assert.Contains("15/10/2026 10:00", textos);
            Assert.DoesNotContain("15/10/2026 13:00", textos);
        }
        finally
        {
            FechaHoraLocalConverter.Zona = zonaAntes;
        }
    }

    // ── Pie fijo ────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(1040.0, 760.0)]
    [InlineData(1126.0, 768.0)]
    public void Pie_FueraDelScroll_ADosTamanos(double ancho, double alto)
    {
        var m = Detalle(ancho: ancho, alto: alto);
        var scroll = Parte<ScrollViewer>(m, "Scroll");
        var pie = Parte<Border>(m, "Pie");

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        Assert.False(scroll.IsVisualAncestorOf(pie), "El pie quedó dentro del scroll.");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "El formulario no scrollea: el pie fijo no se está probando.");
        Assert.Equal(m.Vista.Bounds.Height, EnVentana(m.Ventana, pie).Bottom, 0.5);
        Assert.True(EnVentana(m.Ventana, scroll).Bottom <= EnVentana(m.Ventana, pie).Y + 0.5, "El scroll se mete debajo del pie.");
        var volver = EnVentana(m.Ventana, Parte<Button>(m, "BotonVolver"));
        Assert.True(volver.Y >= 0 && volver.Bottom <= alto + 0.5, "Volver quedó fuera de la ventana.");
    }
}
