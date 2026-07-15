namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>Lee /etc/machine-id (o /var/lib/dbus/machine-id como fallback), id estable de systemd/dbus.</summary>
public sealed class FingerprintMaquinaLinux : FingerprintMaquinaBase
{
    private static readonly string[] Rutas =
    {
        "/etc/machine-id",
        "/var/lib/dbus/machine-id",
    };

    protected override string ObtenerIdCrudo()
    {
        foreach (var ruta in Rutas)
        {
            if (File.Exists(ruta))
            {
                var id = File.ReadAllText(ruta).Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }

        throw new InvalidOperationException(
            "No se pudo leer /etc/machine-id (ni el fallback de dbus).");
    }
}
