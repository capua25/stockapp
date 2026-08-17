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

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // DEUDA ARQUITECTÓNICA (anotada 2026-08-16, no resuelta): este disparo es
            // INCONDICIONAL ante CUALQUIER 403, en la capa de transporte -- no distingue un 403
            // inesperado (violación real de seguridad) de uno ESPERADO que la capa de aplicación
            // ya sabe manejar (ej. MovimientoHistorialViewModel.InicializarAsync chequeando un
            // permiso opcional antes de decidir qué mostrar). Antes de que la excepción llegue a
            // cualquier catch de la aplicación, ya se disparó el modal "No tenés permiso para
            // esta operación" acá. La solución de fondo (bugfix opcombo/Combo2026!,
            // 2026-08-16) fue evitar GENERAR el 403 con un chequeo previo de permiso en el
            // ViewModel -- no tocar este handler. Pero eso es pan-para-hoy: cualquier código que
            // vuelva a "llamar y atrapar" en vez de "chequear antes" va a pisar este mismo modal,
            // porque el aviso vive en la capa de transporte y la capa de aplicación no tiene
            // forma de opinar (no hay mecanismo de supresión por-request, y se descartó
            // deliberadamente agregar uno: invita a tapar errores reales). Si esto se repite una
            // cuarta vez, vale la pena reconsiderar mover la decisión a la capa de aplicación.
            _session.DispararAccesoRevocado();
        }

        return response;
    }
}
