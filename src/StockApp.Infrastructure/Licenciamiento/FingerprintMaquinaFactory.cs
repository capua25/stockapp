using StockApp.Application.Licenciamiento;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>Elige la implementación de fingerprint según el OS del servidor.</summary>
public static class FingerprintMaquinaFactory
{
    public static IFingerprintMaquina Crear()
        => OperatingSystem.IsWindows()
            ? new FingerprintMaquinaWindows()
            : new FingerprintMaquinaLinux();
}
