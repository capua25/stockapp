namespace StockApp.Application.Actualizaciones;

/// <summary>Resultado del chequeo de updates, sin dependencia de Velopack.</summary>
public record UpdateCheckResult(
    bool HayUpdate,
    string? Version,
    UpdateSeverity Severity,
    string? NotasMarkdown)
{
    public static UpdateCheckResult SinUpdate { get; } =
        new(false, null, UpdateSeverity.Normal, null);
}

/// <summary>Progreso de descarga, 0..100.</summary>
public record UpdateProgress(int Porcentaje);

/// <summary>Modo de presentación que la política decide según severity + estado.</summary>
public enum ModoUx
{
    Ninguno,         // no hay update o corriendo en dev
    BannerDiscreto,  // normal
    ModalPosponible, // important
    BloqueoCritico,  // critical, descarga posible
    ModoDegradado    // critical, descarga falló → app usable con banner rojo no-cerrable
}

/// <summary>Acción de UI resultante de la política (severity + estado de descarga).</summary>
public record AccionUx(
    ModoUx Modo,
    string? TextoMarkdown,
    bool Posponible,
    bool ReintentaEnArranque,
    string? Version = null);
