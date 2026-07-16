using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record IngresoCajaWire(
    int Id, DateTime Fecha, string Concepto,
    int FuenteFinanciamientoId, string? FuenteNombre,
    decimal Monto, bool Activo);

internal sealed record IngresoCajaBody(
    DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto);

/// <summary>IIngresoCajaService contra /finanzas/ingresos.</summary>
public sealed class IngresoCajaApiClient : IIngresoCajaService
{
    private readonly HttpClient _http;

    public IngresoCajaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(IngresoCaja ingreso)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/ingresos", ABody(ingreso)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el ingreso.");
        return creado.Id;
    }

    public async Task ModificarAsync(IngresoCaja ingreso)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/ingresos/{ingreso.Id}", ABody(ingreso)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/ingresos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/ingresos"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<IngresoCajaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static IngresoCajaBody ABody(IngresoCaja i) => new(
        i.Fecha, i.Concepto, i.FuenteFinanciamientoId, i.Monto);

    private static IngresoCaja AEntidad(IngresoCajaWire dto) => new()
    {
        Id = dto.Id,
        Fecha = dto.Fecha,
        Concepto = dto.Concepto,
        FuenteFinanciamientoId = dto.FuenteFinanciamientoId,
        FuenteFinanciamiento = dto.FuenteNombre is null
            ? null
            : new FuenteFinanciamiento { Id = dto.FuenteFinanciamientoId, Nombre = dto.FuenteNombre },
        Monto = dto.Monto,
        Activo = dto.Activo,
    };
}
