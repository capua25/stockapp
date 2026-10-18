using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StockApp.ApiClient;
using StockApp.Application.Documentos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.Views.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Panel de adjuntos del documento con la View REAL: no pude consultar no es lo mismo que no hay.</summary>
public class AdjuntosDocumentoPanelViewV2Tests
{
    private static readonly AdjuntoDocumentoDto Pdf = new(1, 7, "contrato.pdf", "application/pdf", 182_400,
        new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc));

    private static (Window Ventana, AdjuntosDocumentoPanelView Vista, AdjuntosDocumentoPanelViewModel Vm, AdjuntosDocumentoFake Servicio,
        ConfirmacionServiceFake Confirmacion) Montar(Exception? falla)
    {
        var servicio = new AdjuntosDocumentoFake { FallaAlListar = falla };
        servicio.Lista.Add(Pdf);
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new AdjuntosDocumentoPanelViewModel(servicio, new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, new SesionFake(RolUsuario.Operador));
        vm.InicializarAsync(7, documentoActivo: true).GetAwaiter().GetResult();
        var vista = new AdjuntosDocumentoPanelView { DataContext = vm };
        var ventana = new Window { Width = 760, Height = 400, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return (ventana, vista, vm, servicio, confirmacion);
    }

    /// <summary>Con la API caída el panel decía "Sin adjuntos" (mentira: no pudo consultar) y abría el modal global.
    /// Ahora: error en línea con Reintentar, sin modal, y sin el texto del vacío.</summary>
    [AvaloniaFact]
    public async Task ApiCaida_ErrorEnLineaConReintentar_SinModalNiSinAdjuntos()
    {
        var m = Montar(new ServidorNoDisponibleException());

        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<EstadoVacio>("EstadoSinConexion")!));
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
        Assert.Empty(m.Confirmacion.MensajesInformados);

        m.Servicio.FallaAlListar = null;
        MontajeV2.Clic(m.Ventana, m.Vista.FindControl<Button>("BotonReintentar")!);
        await (m.Vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<EstadoVacio>("EstadoSinConexion")!));
        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<ItemsControl>("ListaAdjuntos")!));
    }

    [AvaloniaFact]
    public void SinPermiso403_MuestraSinPermisoYOcultaElVacio_SinModal()
    {
        var m = Montar(new UnauthorizedAccessException());

        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<EstadoVacio>("EstadoSinPermiso")!));
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<EstadoVacio>("EstadoSinConexion")!));
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
        Assert.Empty(m.Confirmacion.MensajesInformados);
    }
}
