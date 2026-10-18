using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Optris.Icons.Avalonia;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Panel de adjuntos v2 con la View REAL y el tema real: estructura, vacío, tamaño legible, gates con un
/// operador de permisos mixtos (Admin cortocircuita) y la confirmación al quitar.</summary>
public class AdjuntosPanelViewV2Tests
{
    private static readonly AdjuntoDto Pdf = new(1, "factura-A-0004521.pdf", "application/pdf", 182_400, 42, null,
        new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc));
    private static readonly AdjuntoDto Foto = new(2, "remito-firmado.jpg", "image/jpeg", 1_258_291, 42, null,
        new DateTime(2026, 10, 6, 15, 25, 0, DateTimeKind.Utc));

    private sealed record Montaje(Window Ventana, AdjuntosPanelView Vista, AdjuntosPanelViewModel Vm,
        AdjuntoServiceFake Servicio, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(SesionFake sesion, int? gastoId = 42, int? pagoId = null, params AdjuntoDto[] adjuntos)
    {
        var servicio = new AdjuntoServiceFake();
        servicio.Items.AddRange(adjuntos);
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new AdjuntosPanelViewModel(servicio, new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, sesion);
        vm.InicializarAsync(gastoId, pagoId).GetAwaiter().GetResult();
        var vista = new AdjuntosPanelView { DataContext = vm };
        var ventana = new Window { Width = 760, Height = 400, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, confirmacion);
    }

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    private static Button Boton(Control raiz, string texto) =>
        raiz.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static IEnumerable<Button> Botones(Control raiz, string texto) =>
        raiz.GetVisualDescendants().OfType<Button>().Where(b => (b.Content as string) == texto);

    private static List<string?> Textos(Control raiz) =>
        raiz.GetVisualDescendants().OfType<TextBlock>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void Raiz_TieneUi2_TituloAdjuntos_YAgregarSecundario()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos));

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Adjuntos", m.Vista.FindControl<TextBlock>("TituloAdjuntos")!.Text);
        var agregar = m.Vista.FindControl<Button>("BotonAgregar")!;
        Assert.Contains("secundario", agregar.Classes);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void SinAdjuntos_MuestraElTextoDelVacio_YNoLaLista()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos));

        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
        Assert.Equal("Sin adjuntos. PDF, JPG o PNG hasta 10 MB.", m.Vista.FindControl<TextBlock>("SinAdjuntos")!.Text);
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<ItemsControl>("ListaAdjuntos")!));
    }

    [AvaloniaFact]
    public void ConAdjuntos_TamanoLegibleFechaEIconoPorTipo()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos), adjuntos: [Pdf, Foto]);

        var textos = Textos(m.Vista);
        Assert.Contains("178 KB", textos);
        Assert.Contains("06/10/2026", textos);
        Assert.Contains("1,2 MB", textos);
        Assert.DoesNotContain(textos, t => t is not null && t.EndsWith(" B") && !t.EndsWith("KB") && !t.EndsWith("MB"));
        var iconos = m.Vista.GetVisualDescendants().OfType<Icon>().Select(i => i.Value).ToList();
        Assert.Equal(new[] { "mdi-file-pdf-box", "mdi-file-image-outline" }, iconos);
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
    }

    [AvaloniaFact]
    public void Gates_OperadorMixtoSinElPermisoDelGasto_SoloVer()
    {
        var m = Montar(Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos), adjuntos: [Pdf]);

        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<Button>("BotonAgregar")!));
        Assert.All(Botones(m.Vista, "Quitar"), b => Assert.False(ArbolVisual.EsVisibleEnArbol(b)));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Boton(m.Vista, "Ver")));
    }

    [AvaloniaFact]
    public void Gates_OperadorMixtoConElPermisoDelPago_PanelDePago_Modifica()
    {
        var m = Montar(Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos), gastoId: null, pagoId: 7, adjuntos: [Pdf]);

        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<Button>("BotonAgregar")!));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Boton(m.Vista, "Quitar")));
    }

    /// <summary>Hallazgo B2.1b del GATE 2: con la API caída el panel decía "Sin adjuntos" (mentira: no pudo consultar) y
    /// además abría el modal global. Ahora: error en línea con Reintentar, sin modal, y sin el texto del vacío.</summary>
    [AvaloniaFact]
    public async Task ApiCaida_ErrorEnLineaConReintentar_SinModalNiSinAdjuntos()
    {
        var servicio = new AdjuntoServiceFake { FallaAlListar = new ServidorNoDisponibleException() };
        servicio.Items.Add(Pdf);
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new AdjuntosPanelViewModel(servicio, new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, Mixto(Permisos.RegistrarGastos));
        await vm.InicializarAsync(42, null);
        var vista = new AdjuntosPanelView { DataContext = vm };
        var ventana = new Window { Width = 760, Height = 400, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ArbolVisual.EsVisibleEnArbol(vista.FindControl<EstadoVacio>("EstadoSinConexion")!));
        Assert.False(ArbolVisual.EsVisibleEnArbol(vista.FindControl<TextBlock>("SinAdjuntos")!));
        Assert.Empty(confirmacion.MensajesInformados);

        servicio.FallaAlListar = null;
        MontajeV2.Clic(ventana, vista.FindControl<Button>("BotonReintentar")!);
        await (vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(vista.FindControl<EstadoVacio>("EstadoSinConexion")!));
        Assert.True(ArbolVisual.EsVisibleEnArbol(vista.FindControl<ItemsControl>("ListaAdjuntos")!));
    }

    [AvaloniaFact]
    public async Task Quitar_ConClickReal_PreguntaConElNombreYQuita()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos), adjuntos: [Pdf]);

        MontajeV2.Clic(m.Ventana, Boton(m.Vista, "Quitar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AdjuntosPanelViewModel.MensajeConfirmarQuitar("factura-A-0004521.pdf"),
            Assert.Single(m.Confirmacion.Preguntas));
        Assert.Equal(new[] { 1 }, m.Servicio.Quitados);
        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
    }

    [AvaloniaFact]
    public void NombreLargo_TieneElipsisYTooltipSoloSiSeCorta()
    {
        var largo = Pdf with { NombreArchivo = "factura-A-0004521-barraca-carmelo-materiales-rambla-costanera-octubre-2026.pdf" };
        var m = Montar(Mixto(Permisos.RegistrarGastos), adjuntos: [largo]);

        var nombre = m.Vista.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == largo.NombreArchivo);
        Assert.Equal(TextTrimming.CharacterEllipsis, nombre.TextTrimming);
        Assert.Contains("cuerpo", nombre.Classes);
        Assert.Equal(largo.NombreArchivo, ToolTip.GetTip(nombre));
        Assert.True(StockApp.Presentation.Behaviors.TooltipSiSeCorta.GetActivo(nombre));
    }
}
