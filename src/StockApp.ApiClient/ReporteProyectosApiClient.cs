using System.Net.Http.Json;
using StockApp.Application.Reportes;

namespace StockApp.ApiClient;

/// <summary>IReporteProyectosService contra /reportes/proyectos (reportes.ver). Sin cache local:
/// el servidor tampoco cachea este reporte (D14) -- cada búsqueda pega al servidor.</summary>
public sealed class ReporteProyectosApiClient : IReporteProyectosService
{
    private readonly HttpClient _http;

    public ReporteProyectosApiClient(HttpClient http) => _http = http;

    public async Task<ReporteProyectosDto> ObtenerAsync(FiltroReporteProyectos filtro)
    {
        var query = ApiQuery.Construir(
            ("agrupador", filtro.Agrupador.ToString()),
            ("criterio", filtro.Criterio.ToString()),
            ("desde", filtro.Desde.ToString("O")),
            ("hasta", filtro.Hasta.ToString("O")));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/proyectos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ReporteProyectosDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el reporte de proyectos.");
    }
}
