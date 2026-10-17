using System;
using System.Collections.Generic;
using System.Globalization;
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
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.Views.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de Documentos v2 con la View REAL: alta (breadcrumb, errores por campo, error del servidor) y detalle
/// (transiciones en el encabezado según estado y rol, campos que se deshabilitan con el documento cerrado, historial con
/// hora local, panel de adjuntos y Tareas vinculadas con su gate de permiso), con el pie fijo fuera del scroll.</summary>
public class DocumentoFormViewV2Tests
{
    private static DocumentoAdministrativo Documento(EstadoDocumento estado = EstadoDocumento.EnProceso) => new()
    {
        Id = 9, Numero = "45", Anio = 2026, Tipo = TipoDocumento.Expediente, FechaEmision = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
        Descripcion = "Bacheo de calles céntricas", Estado = estado, RegistradoPorUsuarioId = 1,
        RegistradoPor = new Usuario { Id = 1, NombreUsuario = "mperez" }, FechaRegistro = DateTime.UtcNow,
        Eventos =
        [
            new EventoDocumento { Id = 1, DocumentoAdministrativoId = 9, Texto = "Se pidió presupuesto.", Fecha = new DateTime(2026, 10, 2, 18, 45, 0, DateTimeKind.Utc) },
        ],
    };

    private static AdjuntoDocumentoDto Adjunto() => new(1, 9, "oficio-firmado.pdf", "application/pdf", 245_760, new DateTime(2026, 9, 15, 14, 0, 0, DateTimeKind.Utc));

    private static Tarea Vinculada() => new()
    {
        Id = 4, Titulo = "Reparar el bache", Estado = EstadoTarea.EnCurso, Prioridad = PrioridadTarea.Media, DocumentoAdministrativoId = 9,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow,
    };

    private static SesionFake Admin() => new(RolUsuario.Admin);
    private static SesionFake Mixto(params string[] extra) => new(RolUsuario.Operador, [Permisos.GestionarDocumentos, .. extra]);

    private sealed record Montaje(Window Ventana, DocumentoFormView Vista, DocumentoFormViewModel Vm, DocumentosFake Servicio,
        AdjuntosDocumentoFake Adjuntos, ConfirmacionServiceFake Confirmacion, NavigationRecorderFake Navegacion);

    private static async Task<Montaje> MontarAsync(DocumentoAdministrativo? documento = null, ICurrentSession? sesion = null,
        DocumentosFake? servicio = null, IEnumerable<AdjuntoDocumentoDto>? adjuntos = null, IEnumerable<Tarea>? tareas = null,
        double ancho = 1040, double alto = 760)
    {
        sesion ??= Admin();
        servicio ??= new DocumentosFake();
        var fakeAdjuntos = new AdjuntosDocumentoFake { Lista = (adjuntos ?? []).ToList() };
        var confirmacion = new ConfirmacionServiceFake();
        var navegacion = new NavigationRecorderFake();
        var panel = new AdjuntosDocumentoPanelViewModel(fakeAdjuntos, new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, sesion);
        var tareasFake = new TareasFake { Lista = (tareas ?? []).ToList() };
        var vm = new DocumentoFormViewModel(servicio, sesion, navegacion, confirmacion, panel, tareasFake);
        if (documento is null)
            vm.CargarParaCrear();
        else
        {
            servicio.Activos.Add(documento);
            await vm.CargarParaVerAsync(documento);
        }
        var vista = new DocumentoFormView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, fakeAdjuntos, confirmacion, navegacion);
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

    private static IEnumerable<Control> Campos(Montaje m) =>
    [
        Parte<ComboBox>(m, "ComboTipo"), Parte<TextBox>(m, "CajaNumero"), Parte<NumericUpDown>(m, "SelectorAnio"),
        Parte<CalendarDatePicker>(m, "SelectorFechaEmision"), Parte<TextBox>(m, "CajaDescripcion"),
    ];

    // ── Alta ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Alta_BreadcrumbYGuardarUnicoPrimario()
    {
        var m = await MontarAsync();
        var encabezado = Parte<EncabezadoPagina>(m, "EncabezadoAlta");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.True(Visible(encabezado));
        Assert.False(VisibleNombre(m, "EncabezadoDetalle"));
        Assert.Equal("Documentos administrativos", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo documento", encabezado.BreadcrumbActual);
        Assert.Same(m.Vm.VolverCommand, encabezado.BreadcrumbComando);
        Assert.Same(Parte<Button>(m, "BotonGuardar"), Assert.Single(PrimariosVisibles(m)));
        Assert.False(VisibleNombre(m, "BotonGuardarCambios"));
        Assert.False(VisibleNombre(m, "SeccionDetalle"));
        Assert.All(Campos(m), c => Assert.True(c.IsEffectivelyEnabled));
    }

    [AvaloniaFact]
    public async Task Alta_TocarNumeroYDescripcion_ErroresDebajo()
    {
        var m = await MontarAsync();
        Parte<TextBox>(m, "CajaNumero").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaNumero")));

        Parte<TextBox>(m, "CajaDescripcion").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([DocumentoFormViewModel.MensajeNumeroObligatorio], Errores(Parte<TextBox>(m, "CajaNumero")));
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaDescripcion")));

        Parte<TextBox>(m, "CajaNumero").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([DocumentoFormViewModel.MensajeDescripcionObligatoria], Errores(Parte<TextBox>(m, "CajaDescripcion")));
        Assert.False(m.Vm.GuardarCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Alta_ErrorDelServidor_AlertaArribaDelPie()
    {
        var servicio = new DocumentosFake { FallaAlRegistrar = new ReglaDeNegocioException("Ya existe un documento con ese número.") };
        var m = await MontarAsync(servicio: servicio);
        m.Vm.Numero = "45";
        m.Vm.Descripcion = "Bacheo";
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("Ya existe un documento con ese número.", alerta.Texto);
        Assert.False(Parte<ScrollViewer>(m, "Scroll").IsVisualAncestorOf(alerta));
        Assert.True(EnVentana(m.Ventana, alerta).Bottom <= EnVentana(m.Ventana, Parte<Border>(m, "Pie")).Y + 0.5);
    }

    [AvaloniaTheory]
    [InlineData(1040.0, 760.0)]
    [InlineData(1126.0, 768.0)]
    public async Task Pie_FueraDelScroll_ADosTamanos(double ancho, double alto)
    {
        var m = await MontarAsync(Documento(), Mixto(Permisos.GestionarTareas), adjuntos: [Adjunto()], tareas: [Vinculada()], ancho: ancho, alto: alto);
        var scroll = Parte<ScrollViewer>(m, "Scroll");
        var pie = Parte<Border>(m, "Pie");

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        Assert.False(scroll.IsVisualAncestorOf(pie), "El pie quedó dentro del scroll.");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "El formulario no scrollea: el pie fijo no se está probando.");
        Assert.Equal(m.Vista.Bounds.Height, EnVentana(m.Ventana, pie).Bottom, 0.5);
        Assert.True(EnVentana(m.Ventana, scroll).Bottom <= EnVentana(m.Ventana, pie).Y + 0.5, "El scroll se mete debajo del pie.");
        var guardar = EnVentana(m.Ventana, Parte<Button>(m, "BotonGuardarCambios"));
        Assert.True(guardar.Y >= 0 && guardar.Bottom <= alto + 0.5, "Guardar cambios quedó fuera de la ventana.");
    }

    // ── Detalle ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task DetalleActivo_GuardarCambiosEsElPrimario_YCamposHabilitados()
    {
        var m = await MontarAsync(Documento(), Mixto());

        Assert.True(VisibleNombre(m, "EncabezadoDetalle"));
        Assert.False(VisibleNombre(m, "EncabezadoAlta"));
        Assert.Same(Parte<Button>(m, "BotonGuardarCambios"), Assert.Single(PrimariosVisibles(m)));
        Assert.False(VisibleNombre(m, "BotonGuardar"));
        Assert.All(Campos(m), c => Assert.True(c.IsEffectivelyEnabled));
        Assert.Equal("Registrado por: mperez", Parte<TextBlock>(m, "RegistradoPor").Text);
        var insignia = Parte<Insignia>(m, "InsigniaEstado");
        Assert.Equal("En proceso", insignia.Texto);
        Assert.Equal(VarianteInsignia.Advertencia, insignia.Variante);
    }

    [AvaloniaFact]
    public async Task DetalleCerrado_CamposDeshabilitadosSinGuardarCambios_ConReabrirParaAdmin()
    {
        var m = await MontarAsync(Documento(EstadoDocumento.Finalizado), Admin());

        Assert.All(Campos(m), c => Assert.False(c.IsEffectivelyEnabled));
        Assert.Empty(PrimariosVisibles(m));
        Assert.False(VisibleNombre(m, "BotonGuardarCambios"));
        Assert.True(VisibleNombre(m, "BotonReabrir"));
        Assert.Equal("Finalizado", Parte<Insignia>(m, "InsigniaEstado").Texto);
    }

    [AvaloniaFact]
    public async Task Detalle_TransicionesEnElEncabezadoSegunEstadoYRol()
    {
        var mixto = await MontarAsync(Documento(EstadoDocumento.EnProceso), Mixto());
        Assert.True(VisibleNombre(mixto, "BotonVolverAPendiente"));
        Assert.True(VisibleNombre(mixto, "BotonFinalizar"));
        Assert.False(VisibleNombre(mixto, "BotonAnular"));
        Assert.False(VisibleNombre(mixto, "BotonIniciar"));
        Assert.False(VisibleNombre(mixto, "BotonReabrir"));

        var admin = await MontarAsync(Documento(EstadoDocumento.EnProceso), Admin());
        Assert.True(VisibleNombre(admin, "BotonAnular"));

        var pendiente = await MontarAsync(Documento(EstadoDocumento.Pendiente), Mixto());
        Assert.True(VisibleNombre(pendiente, "BotonIniciar"));
        Assert.False(VisibleNombre(pendiente, "BotonFinalizar"));
    }

    [AvaloniaFact]
    public async Task Detalle_OperadorConTareasGestionar_TareasVinculadasVisible()
    {
        var m = await MontarAsync(Documento(), Mixto(Permisos.GestionarTareas), tareas: [Vinculada()]);

        Assert.True(VisibleNombre(m, "SeccionTareasVinculadas"));
        var insignia = Parte<ItemsControl>(m, "ListaTareasVinculadas").GetVisualDescendants().OfType<Insignia>().Single();
        Assert.Equal("En curso", insignia.Texto);
        Assert.Equal(VarianteInsignia.Advertencia, insignia.Variante);
    }

    [AvaloniaFact]
    public async Task Detalle_OperadorSinTareasGestionar_TareasVinculadasOculta()
    {
        var m = await MontarAsync(Documento(), Mixto(), tareas: [Vinculada()]);

        Assert.False(VisibleNombre(m, "SeccionTareasVinculadas"));
        Assert.True(VisibleNombre(m, "SeccionAdjuntos"));
        Assert.True(VisibleNombre(m, "SeccionHistorial"));
    }

    [AvaloniaFact]
    public async Task RevocacionDeTareasGestionar_ConLaFichaAbierta_OcultaTareasVinculadas()
    {
        var sesion = Mixto(Permisos.GestionarTareas);
        var tras = new HashSet<string> { Permisos.GestionarDocumentos };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new AdjuntosDocumentoPanelViewModel(new AdjuntosDocumentoFake(), new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), confirmacion, sesion);
        var vm = new DocumentoFormViewModel(new DocumentosFake(), sesion, new NavigationRecorderFake(), confirmacion, panel,
            new TareasFake { Lista = [Vinculada()] });
        await vm.CargarParaVerAsync(Documento());
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
        Control Seccion() => MontajeV2.Parte<Control>(ventana.GetVisualDescendants().OfType<DocumentoFormView>().Single(), "SeccionTareasVinculadas");
        Assert.True(ArbolVisual.EsVisibleEnArbol(Seccion()), "Precondición: con tareas.gestionar se ve la sección.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(Seccion()));
    }

    [AvaloniaFact]
    public async Task Evento_FechaEnHoraLocal()
    {
        var m = await MontarAsync(Documento(), Mixto());
        var evento = m.Vm.Eventos.Single();
        var esperado = (string)FechaHoraLocalConverter.Instance.Convert(evento.Fecha, typeof(string), null, CultureInfo.InvariantCulture)!;

        var textos = Parte<ItemsControl>(m, "ListaEventos").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains(esperado, textos);
        if (TimeZoneInfo.Local.BaseUtcOffset != TimeSpan.Zero)
            Assert.DoesNotContain(evento.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), textos);
    }

    [AvaloniaFact]
    public async Task Anular_PideElMotivoConPedirTexto()
    {
        var m = await MontarAsync(Documento(EstadoDocumento.EnProceso), Admin());

        Clickear(m.Ventana, Parte<Button>(m, "BotonAnular"));

        Assert.Equal("Anular documento", Assert.Single(m.Confirmacion.PedidosDeTexto).Titulo);
        Assert.Equal(("Anular documento", "Ingresá el motivo de la anulación:"), m.Confirmacion.PedidosDeTexto[0]);
        Assert.Equal(9, Assert.Single(m.Servicio.Anulados).Id);
    }

    // ── Panel de adjuntos ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Adjuntos_DocumentoActivoComoOperador_AgregarVisibleQuitarOculto()
    {
        var m = await MontarAsync(Documento(), Mixto(), adjuntos: [Adjunto()]);

        Assert.True(VisibleNombre(m, "BotonAgregar"));
        Assert.DoesNotContain(Parte<ItemsControl>(m, "ListaAdjuntos").GetVisualDescendants().OfType<Button>(),
            b => b.Content as string == "Quitar" && Visible(b));
        Assert.Contains(Parte<ItemsControl>(m, "ListaAdjuntos").GetVisualDescendants().OfType<Button>(),
            b => b.Content as string == "Ver" && Visible(b));
    }

    [AvaloniaFact]
    public async Task Adjuntos_DocumentoActivoComoAdmin_AgregarYQuitarVisibles()
    {
        var m = await MontarAsync(Documento(), Admin(), adjuntos: [Adjunto()]);

        Assert.True(VisibleNombre(m, "BotonAgregar"));
        Assert.Contains(Parte<ItemsControl>(m, "ListaAdjuntos").GetVisualDescendants().OfType<Button>(),
            b => b.Content as string == "Quitar" && Visible(b));
    }

    [AvaloniaFact]
    public async Task Adjuntos_DocumentoCerrado_SinAgregarNiQuitar()
    {
        var m = await MontarAsync(Documento(EstadoDocumento.Finalizado), Admin(), adjuntos: [Adjunto()]);

        Assert.False(VisibleNombre(m, "BotonAgregar"));
        Assert.DoesNotContain(Parte<ItemsControl>(m, "ListaAdjuntos").GetVisualDescendants().OfType<Button>(),
            b => b.Content as string == "Quitar" && Visible(b));
    }

    [AvaloniaFact]
    public async Task Adjuntos_TamanoLegibleYFechaCorta()
    {
        var m = await MontarAsync(Documento(), Mixto(), adjuntos: [Adjunto()]);

        var textos = Parte<ItemsControl>(m, "ListaAdjuntos").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("240 KB", textos);
        var fechaLocal = (string)FechaUtcACortaConverter.Instance.Convert(Adjunto().FechaAltaUtc, typeof(string), null, CultureInfo.InvariantCulture)!;
        Assert.Contains(fechaLocal, textos);
        Assert.Contains("oficio-firmado.pdf", textos);
    }

    [AvaloniaFact]
    public async Task Adjuntos_ClickEnQuitar_PideConfirmacionConElTexto()
    {
        var m = await MontarAsync(Documento(), Admin(), adjuntos: [Adjunto()]);
        var quitar = Parte<ItemsControl>(m, "ListaAdjuntos").GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Quitar");

        quitar.BringIntoView(); // el panel queda bajo el pliegue del scroll
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, quitar);

        Assert.Equal("¿Quitar el adjunto \"oficio-firmado.pdf\"?", Assert.Single(m.Confirmacion.Preguntas));
        Assert.Equal([1], m.Adjuntos.Quitados);
    }

    [AvaloniaFact]
    public async Task Adjuntos_SinArchivos_TextoDeVacio()
    {
        var m = await MontarAsync(Documento(), Mixto());

        Assert.True(VisibleNombre(m, "SinAdjuntos"));
        Assert.Equal("Sin adjuntos. PDF, JPG o PNG hasta 10 MB.", Parte<TextBlock>(m, "SinAdjuntos").Text);
        Assert.False(VisibleNombre(m, "ListaAdjuntos"));
    }
}
