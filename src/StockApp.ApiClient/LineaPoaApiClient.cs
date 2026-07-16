using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record AsignacionPresupuestalWire(
    int Id, int FuenteFinanciamientoId, string? FuenteFinanciamientoNombre, decimal Monto);
internal sealed record LineaPoaWire(
    int Id, string Nombre, string Programa, int Ejercicio, bool Activo,
    List<AsignacionPresupuestalWire> Asignaciones);
internal sealed record AsignacionPresupuestalBody(int FuenteFinanciamientoId, decimal Monto);
internal sealed record LineaPoaBody(
    string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalBody> Asignaciones);

/// <summary>
/// ILineaPoaService contra /finanzas/lineas-poa. El agregado viaja completo: alta y
/// modificación mandan la línea CON su lista de asignaciones presupuestales.
/// </summary>
public sealed class LineaPoaApiClient : ILineaPoaService
{
    private readonly HttpClient _http;

    public LineaPoaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(LineaPoa linea)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/lineas-poa", ABody(linea)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la línea POA.");
        return creado.Id;
    }

    public async Task ModificarAsync(LineaPoa linea)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/lineas-poa/{linea.Id}", ABody(linea)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/lineas-poa/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => ListarAsync("finanzas/lineas-poa");

    public Task<IReadOnlyList<LineaPoa>> ListarActivasAsync() => ListarAsync("finanzas/lineas-poa/activas");

    private async Task<IReadOnlyList<LineaPoa>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<LineaPoaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static LineaPoaBody ABody(LineaPoa linea) => new(
        linea.Nombre, linea.Programa, linea.Ejercicio,
        linea.Asignaciones
            .Select(a => new AsignacionPresupuestalBody(a.FuenteFinanciamientoId, a.Monto))
            .ToList());

    private static LineaPoa AEntidad(LineaPoaWire dto) => new()
    {
        Id = dto.Id,
        Nombre = dto.Nombre,
        Programa = dto.Programa,
        Ejercicio = dto.Ejercicio,
        Activo = dto.Activo,
        Asignaciones = dto.Asignaciones
            .Select(a => new AsignacionPresupuestal
            {
                Id = a.Id,
                LineaPoaId = dto.Id,
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
                // El nombre de la fuente se materializa en la nav para que la grilla
                // del desktop lo muestre sin otra llamada.
                FuenteFinanciamiento = a.FuenteFinanciamientoNombre is null
                    ? null
                    : new FuenteFinanciamiento
                    {
                        Id = a.FuenteFinanciamientoId,
                        Nombre = a.FuenteFinanciamientoNombre,
                    },
            })
            .ToList(),
    };
}
