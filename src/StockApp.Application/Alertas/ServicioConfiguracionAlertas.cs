using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

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
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parseada))
                throw new ArgumentException("La URL del webhook debe ser una URL absoluta.");

            if (parseada.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("La URL del webhook debe usar https.");
        }

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
    /// Dispara un ping real contra la URL configurada, como si fuera una corrida exitosa. Es el
    /// núcleo de la funcionalidad: un canal de alerta que nunca se probó no es un canal, es una
    /// creencia — la URL mal escrita se descubriría recién el día del fallo.
    /// </summary>
    public async Task<ResultadoPruebaAlertaDto> ProbarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var cfg = await _repo.ObtenerAsync();

        if (string.IsNullOrWhiteSpace(cfg.UrlWebhook))
            return new ResultadoPruebaAlertaDto(false, null, "No hay una URL de webhook configurada.");

        if (!cfg.Habilitado)
            return new ResultadoPruebaAlertaDto(false, null, "El canal de alerta está deshabilitado.");

        await _notificador.NotificarCorridaBackupAsync(new CorridaBackup
        {
            IniciadaEn = DateTime.UtcNow,
            FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Exitosa,
            NombreArchivo = "prueba-de-canal",
            TamanioBytes = 0,
        });

        // El notificador se traga los errores por contrato, así que este resultado confirma que
        // el ping se intentó, no que el servidor remoto lo haya aceptado. El detalle fino queda
        // en el log del servidor, descargable desde la misma pantalla de Mantenimiento.
        return new ResultadoPruebaAlertaDto(true, null, "Se envió un ping de prueba al webhook configurado.");
    }
}
