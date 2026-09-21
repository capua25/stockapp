using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Exportacion;
using StockApp.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.Arranque;

/// <summary>
/// Fix ronda 1/5 (Tarea 9, spec 2026-09-18): <see cref="StockApp.Presentation.App.ConfigurarServicios"/>
/// pasó de <c>private</c> a <c>internal</c> -- mismo seam que <c>App.ConstruirConfiguracion</c> /
/// <c>App.ResolverApiBaseUrl</c> / <c>App.ManejarExcepcionUiThread</c> (ver
/// ResolucionApiBaseUrlTests / ManejoExcepcionesGlobalesTests) -- para que este test invoque el
/// armado REAL del contenedor de producción, no un mirror manual.
///
/// Antecedente que motivó el cambio: <c>ComposicionDIApiTests</c> (en <c>tests/.../DI/</c>) es un
/// mirror manual de <c>ConfigurarServicios</c> escrito antes de este fix -- reproduce a mano las
/// líneas de registro en vez de invocar el método real. Verificado por mutación (ver reporte de
/// la Tarea 9, sección "Fix ronda 1/5"): borrar la línea REAL de <c>App.axaml.cs</c> dejaba ese
/// mirror en VERDE, exactamente el mismo síntoma documentado para los tests de gate de permisos
/// de UI que seguían en verde al sacar el <c>IsVisible</c> del XAML -- un test que certifica su
/// propia copia, no el sistema. Este archivo llama a <c>App.ConfigurarServicios()</c> directamente
/// para que la mutación real SÍ se detecte.
/// </summary>
public class ConfigurarServiciosTests
{
    [Fact]
    public void ConfigurarServicios_Resuelve_IPdfExporter_ConPdfExporterMigraDoc()
    {
        using var sp = StockApp.Presentation.App.ConfigurarServicios();

        var servicio = sp.GetRequiredService<IPdfExporter>();

        Assert.IsType<PdfExporterMigraDoc>(servicio);
    }
}
