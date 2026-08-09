namespace StockApp.Domain.Entities;

/// <summary>
/// Configuración del canal de alerta de backups. Tabla de FILA ÚNICA (Id = 1): no hay
/// múltiples configuraciones, hay una sola instalación. Es la primera configuración del
/// sistema persistida en base — el resto vive en appsettings.json, inaccesible después de
/// la instalación (no hay acceso al servidor), y por eso esta tiene que estar en la base.
/// </summary>
public class ConfiguracionAlertas
{
    /// <summary>Siempre 1. La fila única se siembra en la migración.</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// URL del webhook a pinguear (convención healthchecks.io: éxito a la URL, fallo a
    /// {url}/fail). Null = sin configurar. Se valida https en ServicioConfiguracionAlertas.
    /// </summary>
    public string? UrlWebhook { get; set; }

    /// <summary>Interruptor explícito: con URL cargada pero Habilitado = false, no se notifica.</summary>
    public bool Habilitado { get; set; }

    /// <summary>UTC. Cuándo se guardó por última vez.</summary>
    public DateTime ActualizadoEn { get; set; }

    /// <summary>
    /// Quién guardó la configuración por última vez. Null si nunca se tocó (fila sembrada).
    /// FK Restrict a Usuarios, mismo criterio que CorridaBackup.UsuarioId y NotaTarea.Usuario.
    /// </summary>
    public int? ActualizadoPorUsuarioId { get; set; }

    public Usuario? Usuario { get; set; }
}
