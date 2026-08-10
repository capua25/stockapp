using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Alertas;

/// <summary>
/// Lectura, validación y guardado del canal de alerta, más el ping de prueba. Segunda barrera de
/// autorización (defensa en profundidad): la policy HTTP ya exige GestionarDiagnostico, pero el
/// servicio lo verifica igual, mismo criterio que el resto de los servicios de Application.
/// </summary>
public sealed class ServicioConfiguracionAlertas
{
    private readonly IConfiguracionAlertasRepository _repo;
    private readonly IAuthorizationService _auth;
    private readonly ICurrentSession _session;
    private readonly INotificadorAlertas _notificador;

    public ServicioConfiguracionAlertas(
        IConfiguracionAlertasRepository repo,
        IAuthorizationService auth,
        ICurrentSession session,
        INotificadorAlertas notificador)
    {
        _repo = repo;
        _auth = auth;
        _session = session;
        _notificador = notificador;
    }

    public async Task<ConfiguracionAlertasDto> ObtenerAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var cfg = await _repo.ObtenerAsync();
        return new ConfiguracionAlertasDto(
            cfg.UrlWebhook,
            cfg.Habilitado,
            cfg.ActualizadoEn == default ? null : cfg.ActualizadoEn);
    }

    public async Task GuardarAsync(string? urlWebhook, bool habilitado)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var url = string.IsNullOrWhiteSpace(urlWebhook) ? null : urlWebhook.Trim();

        if (url is not null)
            ValidarUrl(url);

        // Habilitar sin URL es una configuración que miente: el interruptor queda en "sí" y no
        // notifica nada. Se rechaza en vez de guardar un estado engañoso.
        if (habilitado && url is null)
            throw new ArgumentException("No se puede habilitar el canal de alerta sin una URL de webhook.");

        var cfg = await _repo.ObtenerAsync();
        cfg.UrlWebhook = url;
        cfg.Habilitado = habilitado;
        cfg.ActualizadoEn = DateTime.UtcNow;
        cfg.ActualizadoPorUsuarioId = _session.UsuarioActual?.Id;

        await _repo.GuardarAsync(cfg);
    }

    /// <summary>
    /// Dispara un ping REAL y devuelve el status code obtenido (spec §4). Es el núcleo de la
    /// funcionalidad: un canal de alerta que nunca se probó no es un canal, es una creencia — la
    /// URL mal escrita se descubriría recién el día del fallo.
    ///
    /// <paramref name="urlWebhook"/> (fix del review final): si viene, se prueba ESA — la que el
    /// usuario tiene en pantalla — sin persistirla. El flujo natural es pegar la URL y apretar
    /// Probar; antes se pingueaba siempre la guardada, o sea la URL VIEJA (o ninguna), y el
    /// verificador contestaba sobre una configuración distinta de la que el usuario estaba
    /// mirando. Si no viene, se usa la guardada.
    ///
    /// La validación de la URL de pantalla es la MISMA que la de <see cref="GuardarAsync"/> y no
    /// es negociable: la superficie SSRF es idéntica (el servidor postea a una URL provista por
    /// el usuario), así que "probar sin guardar" no puede ser la puerta de atrás que saltea el
    /// gate de absoluta + https.
    /// </summary>
    public async Task<ResultadoPruebaAlertaDto> ProbarAsync(
        string? urlWebhook = null, CancellationToken ct = default)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var urlEnPantalla = string.IsNullOrWhiteSpace(urlWebhook) ? null : urlWebhook.Trim();

        if (urlEnPantalla is not null)
        {
            ValidarUrl(urlEnPantalla);

            // A propósito NO se mira cfg.Habilitado acá: probar ANTES de habilitar es
            // exactamente el orden en que un humano configura esto por primera vez.
            return await _notificador.ProbarPingAsync(urlEnPantalla, ct);
        }

        var cfg = await _repo.ObtenerAsync();

        if (string.IsNullOrWhiteSpace(cfg.UrlWebhook))
            return new ResultadoPruebaAlertaDto(false, null, "No hay una URL de webhook configurada.");

        if (!cfg.Habilitado)
            return new ResultadoPruebaAlertaDto(false, null, "El canal de alerta está deshabilitado.");

        return await _notificador.ProbarPingAsync(cfg.UrlWebhook!, ct);
    }

    /// <summary>
    /// Gate de SSRF compartido por GuardarAsync y ProbarAsync: absoluta + https, sin excepciones.
    /// Extraído para que las dos entradas por las que una URL provista por el usuario llega a un
    /// POST del servidor no puedan divergir.
    /// </summary>
    private static void ValidarUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parseada))
            throw new ArgumentException("La URL del webhook debe ser una URL absoluta.");

        if (parseada.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("La URL del webhook debe usar https.");
    }
}
