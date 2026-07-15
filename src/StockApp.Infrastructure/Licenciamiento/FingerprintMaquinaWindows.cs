using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>Lee HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (id estable por instalación de Windows).</summary>
[SupportedOSPlatform("windows")]
public sealed class FingerprintMaquinaWindows : FingerprintMaquinaBase
{
    protected override string ObtenerIdCrudo()
    {
        using var key = RegistryKey
            .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

        var guid = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidOperationException(
                "No se pudo leer MachineGuid del registro de Windows.");

        return guid;
    }
}
