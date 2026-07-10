using System.Net.Http.Json;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

internal sealed record CrearUsuarioBody(
    string NombreUsuario, string? NombreCompleto, string ContrasenaPlan, RolUsuario Rol);
internal sealed record CambiarRolBody(RolUsuario NuevoRol);
internal sealed record CambiarContrasenaBody(string NuevaContrasena, string? ContrasenaActual);

/// <summary>IUsuarioService contra /usuarios (Admin-only; el alta devuelve el id — 3a, D2).</summary>
public sealed class UsuarioApiClient : IUsuarioService
{
    private readonly HttpClient _http;

    public UsuarioApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync(
            "usuarios", new CrearUsuarioBody(nombreUsuario, nombreCompleto, contrasenaPlan, rol)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el usuario.");
        return creado.Id;
    }

    public async Task BajaLogicaAsync(int usuarioId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"usuarios/{usuarioId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"usuarios/{usuarioId}/rol", new CambiarRolBody(nuevoRol)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarContrasenaAsync(
        int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync(
            $"usuarios/{usuarioId}/contrasena",
            new CambiarContrasenaBody(nuevaContrasenaPlan, contrasenaActualPlan)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("usuarios"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<UsuarioDto>>() ?? new();
    }
}
