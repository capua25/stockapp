// src/StockApp.ApiClient/PrimerArranqueApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Auth;

namespace StockApp.ApiClient;

internal sealed record PrimerArranqueEstadoWire(bool RequiereCrearAdmin);
internal sealed record CrearAdminInicialBody(string NombreUsuario, string Contrasena);

/// <summary>
/// IPrimerArranqueService contra los endpoints bootstrap anónimos de 3a (D7):
/// GET /auth/primer-arranque y POST /auth/primer-admin. PrimerArranqueViewModel no cambia.
/// </summary>
public sealed class PrimerArranqueApiClient : IPrimerArranqueService
{
    private readonly HttpClient _http;

    public PrimerArranqueApiClient(HttpClient http) => _http = http;

    public async Task<bool> RequiereCrearAdminAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("auth/primer-arranque"));
        await ApiErrores.AsegurarExitoAsync(response);

        var estado = await response.Content.ReadFromJsonAsync<PrimerArranqueEstadoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el bootstrap.");
        return estado.RequiereCrearAdmin;
    }

    public async Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("auth/primer-admin", new CrearAdminInicialBody(nombreUsuario, contrasenaPlana)));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
