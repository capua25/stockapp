using System.Net.Http.Json;
using StockApp.Application.Reportes;

namespace StockApp.ApiClient;

/// <summary>IReporteTareasService contra /reportes/tareas (reportes.ver). Sin cache local:
/// el servidor tampoco cachea este reporte (D14) -- cada búsqueda pega al servidor.</summary>
public sealed class ReporteTareasApiClient : IReporteTareasService
{
    private readonly HttpClient _http;

    public ReporteTareasApiClient(HttpClient http) => _http = http;

    public async Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro)
    {
        var query = ApiQuery.Construir(
            ("agrupador", filtro.Agrupador.ToString()),
            ("criterio", filtro.Criterio.ToString()),
            ("desde", filtro.Desde.ToString("O")),
            ("hasta", filtro.Hasta.ToString("O")));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/tareas" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ReporteTareasDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el reporte de tareas.");
    }
}
