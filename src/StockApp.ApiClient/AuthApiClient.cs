// src/StockApp.ApiClient/AuthApiClient.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.Application.Auth;

namespace StockApp.ApiClient;

internal sealed record LoginBody(string NombreUsuario, string Contrasena);
internal sealed record UsuarioLoginWire(int Id, string NombreUsuario, string? NombreCompleto, StockApp.Domain.Enums.RolUsuario Rol);
internal sealed record LoginRespuestaWire(string Token, UsuarioLoginWire Usuario);

/// <summary>
/// IAuthService contra POST /auth/login (3a, D8: LoginResponse enriquecido). Puebla
/// ApiSession con el snapshot del usuario + token; el 401 del login se traduce a
/// LoginResult.Fallo (la UI muestra un único mensaje genérico — anti-enumeración).
/// </summary>
public sealed class AuthApiClient : IAuthService
{
    private readonly HttpClient _http;
    private readonly ApiSession _session;

    public AuthApiClient(HttpClient http, ApiSession session)
    {
        _http    = http;
        _session = session;
    }

    public async Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
    {
        // Un login nuevo invalida cualquier sesión previa (y evita adjuntar un token viejo).
        _session.CerrarSesion();

        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("auth/login", new LoginBody(nombreUsuario, contrasena)));

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // El servidor no distingue usuario inexistente / contraseña mala / inactivo
            // (anti-enumeración) y la UI tampoco: cualquier LoginError produce el mismo mensaje.
            return LoginResult.Fallo(LoginError.ContrasenaInvalida);
        }

        await ApiErrores.AsegurarExitoAsync(response);

        var body = await response.Content.ReadFromJsonAsync<LoginRespuestaWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el login.");

        _session.EstablecerSesion(
            new UsuarioSesion(body.Usuario.Id, body.Usuario.NombreUsuario, body.Usuario.Rol, body.Usuario.NombreCompleto),
            body.Token);

        return LoginResult.Ok();
    }

    public Task LogoutAsync()
    {
        // JWT sin estado: no hay endpoint de logout; alcanza con descartar el token local.
        _session.CerrarSesion();
        return Task.CompletedTask;
    }
}
