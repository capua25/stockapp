using System.Net;
using System.Net.Http.Headers;

namespace StockApp.ApiClient;

/// <summary>
/// Adjunta `Authorization: Bearer` a cada request con el token de ApiSession, y detecta
/// la sesión vencida en UN solo lugar (spec 3b): un 401 a un request QUE LLEVABA token
/// cierra la sesión y dispara ApiSession.SesionVencida (el Shell navega al login con
/// aviso). El 401 del login (sin token, credenciales malas) NO dispara el evento.
/// </summary>
public class AuthTokenHandler : DelegatingHandler
{
    private readonly ApiSession _session;

    public AuthTokenHandler(ApiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _session.Token;
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
        {
            _session.CerrarSesion();
            _session.DispararSesionVencida();
        }

        if (response.StatusCode == (HttpStatusCode)423)
        {
            _session.DispararLicenciaDesactivada();
        }

        return response;
    }
}
