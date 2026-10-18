using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;
using static StockApp.Presentation.UiTests.V2.DatosDeReportes;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Estados del marco común con la View REAL de Valorización (la más simple): breadcrumb, cargando, sin conexión, sin permiso,
/// vacío, alerta de error, filtros opcionales y nada de botones primarios.</summary>
public class MarcoReporteV2Tests
{
    private static T Parte<T>(Control raiz, string nombre) where T : Control => MontajeV2.Parte<T>(raiz, nombre);
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    [AvaloniaFact]
    public void Breadcrumb_ReportesYElTitulo_ElPrimerTramoVuelveAlHub()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake { Valorizacion = { Cemento } });

        var encabezado = Parte<EncabezadoPagina>(m.Vista, "Encabezado");
        Assert.Equal("Reportes", encabezado.BreadcrumbOrigen);
        Assert.Equal("Valorización de inventario", encabezado.BreadcrumbActual);
        Assert.Equal("Valorización de inventario", encabezado.Titulo);
        Assert.Null(encabezado.Subtitulo);

        MontajeV2.Clic(m.Ventana, Parte<Button>(encabezado, "PART_BreadcrumbOrigen"));

        Assert.Equal(typeof(ReportesViewModel), m.Navegacion.UltimoDestino);
    }

    [AvaloniaFact]
    public void Cargando_SinFilas_MuestraElIndicador_SinVacioNiGrilla()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake { PendienteValorizacion = new TaskCompletionSource<StockApp.Application.Reportes.ValorizacionReporteDto>() });

        Assert.True(Visible(Parte<EstadoCargando>(m.Vista, "IndicadorCarga")));
        Assert.False(Visible(Parte<EstadoVacio>(m.Vista, "EstadoVacio")));
        Assert.False(Visible(Parte<ContentControl>(m.Vista, "ZonaContenido")));
    }

    [AvaloniaFact]
    public async Task Cargando_ConFilas_NoMuestraElIndicador()
    {
        var servicio = new ReporteStockFake { Valorizacion = { Cemento } };
        var m = MontajeReportes.Valorizacion(servicio);
        Assert.Single(m.Vm.Items);

        servicio.PendienteValorizacion = new TaskCompletionSource<StockApp.Application.Reportes.ValorizacionReporteDto>();
        var recarga = m.Vm.CargarAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.Cargando);
        Assert.False(Visible(Parte<EstadoCargando>(m.Vista, "IndicadorCarga")));
        Assert.True(Visible(Parte<ContentControl>(m.Vista, "ZonaContenido")));

        servicio.PendienteValorizacion.SetResult(new(new[] { Cemento }, new(Cemento.ValorCosto)));
        await recarga;
    }

    [AvaloniaFact]
    public void SinConexion_EstadoEnLineaConReintentar_SinModalGlobal()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake { FallaValorizacion = new ServidorNoDisponibleException() });

        Assert.True(Visible(Parte<EstadoVacio>(m.Vista, "EstadoSinConexion")));
        Assert.True(Visible(Parte<Button>(m.Vista, "BotonReintentar")));
        Assert.False(Visible(Parte<EstadoVacio>(m.Vista, "EstadoVacio")));
        Assert.False(Visible(Parte<ContentControl>(m.Vista, "ZonaContenido")));
        Assert.Empty(m.Confirmacion.MensajesInformados);
    }

    [AvaloniaFact]
    public void SinConexion_ReintentarConClickReal_CargaElReporte()
    {
        var servicio = new ReporteStockFake { FallaValorizacion = new ServidorNoDisponibleException() };
        var m = MontajeReportes.Valorizacion(servicio);
        servicio.FallaValorizacion = null;
        servicio.Valorizacion.Add(Cemento);

        MontajeV2.Clic(m.Ventana, Parte<Button>(m.Vista, "BotonReintentar"));
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<EstadoVacio>(m.Vista, "EstadoSinConexion")));
        Assert.True(Visible(Parte<ContentControl>(m.Vista, "ZonaContenido")));
    }

    [AvaloniaFact]
    public void SinPermiso_ReemplazaLaCard()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake { FallaValorizacion = new System.UnauthorizedAccessException() });

        var estado = Parte<EstadoVacio>(m.Vista, "EstadoSinPermiso");
        Assert.True(Visible(estado));
        Assert.Equal("Sin permiso", estado.Titulo);
        Assert.Equal(m.Vm.MensajeSinPermiso, estado.Mensaje);
        // La card entera (con su vacío y su grilla) queda oculta: lo de adentro ni se arma.
        Assert.False(Visible(Parte<TarjetaContenido>(m.Vista, "Tarjeta")));
    }

    [AvaloniaFact]
    public void Vacio_SoloConLaCargaTerminadaYSinError()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake());

        var vacio = Parte<EstadoVacio>(m.Vista, "EstadoVacio");
        Assert.True(Visible(vacio));
        Assert.Equal("No hay productos para valorizar", vacio.Titulo);
        Assert.Equal("Cuando haya productos activos, su valor aparece acá.", vacio.Mensaje);
        Assert.False(Visible(Parte<ContentControl>(m.Vista, "ZonaPie")));

        // Con Cargando, SinPermiso o SinConexion el vacío no aparece.
        var cargando = MontajeReportes.Valorizacion(new ReporteStockFake { PendienteValorizacion = new TaskCompletionSource<StockApp.Application.Reportes.ValorizacionReporteDto>() });
        Assert.False(Visible(Parte<EstadoVacio>(cargando.Vista, "EstadoVacio")));
        var permiso = MontajeReportes.Valorizacion(new ReporteStockFake { FallaValorizacion = new System.UnauthorizedAccessException() });
        Assert.False(Visible(Parte<TarjetaContenido>(permiso.Vista, "Tarjeta")));
        var conexion = MontajeReportes.Valorizacion(new ReporteStockFake { FallaValorizacion = new ServidorNoDisponibleException() });
        Assert.False(Visible(Parte<EstadoVacio>(conexion.Vista, "EstadoVacio")));
    }

    [AvaloniaFact]
    public void MensajeError_MuestraLaAlertaDeError()
    {
        var m = MontajeReportes.MasMovidos(new ReporteStockFake { MasMovidos = { new(1, "MAT-0142", "Cemento", 3, 10m) } });
        Assert.False(Visible(Parte<Alerta>(m.Vista, "AlertaError")));

        m.Vm.FechaDesde = new System.DateTime(2026, 9, 30);
        m.Vm.FechaHasta = new System.DateTime(2026, 9, 1);
        m.Vm.BuscarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var alerta = Parte<Alerta>(m.Vista, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("La fecha 'Desde' no puede ser posterior a 'Hasta'.", alerta.Texto);
    }

    [AvaloniaFact]
    public void SinFiltros_NoHayBarraDeFiltros()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake { Valorizacion = { Cemento } });
        var conFiltros = MontajeReportes.MasMovidos(new ReporteStockFake());

        Assert.False(Visible(Parte<BarraFiltros>(m.Vista, "BarraDeFiltros")));
        Assert.True(Visible(Parte<BarraFiltros>(conFiltros.Vista, "BarraDeFiltros")));
    }

    [AvaloniaFact]
    public void NingunBotonPrimario()
    {
        var m = MontajeReportes.Valorizacion(new ReporteStockFake { Valorizacion = { Cemento } });

        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
        Assert.Contains("secundario", Parte<Button>(m.Vista, "BotonActualizar").Classes);
        Assert.Contains("secundario", Parte<Button>(m.Vista, "BotonExportarPdf").Classes);
    }
}
