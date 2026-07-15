using System.Security.Cryptography;
using System.Text;
using StockApp.Application.Licenciamiento;

namespace StockApp.Infrastructure.Licenciamiento;

/// <summary>
/// Hashea el id crudo del OS con SHA-256 y lo presenta en hex mayúsculas agrupado de a 4
/// con guiones. Las subclases solo aportan de dónde sale el id crudo.
/// </summary>
public abstract class FingerprintMaquinaBase : IFingerprintMaquina
{
    public string CodigoAgrupado
    {
        get
        {
            var idCrudo = ObtenerIdCrudo();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idCrudo));
            var hex = Convert.ToHexString(hash); // 64 chars, mayúsculas

            var sb = new StringBuilder(hex.Length + hex.Length / 4);
            for (var i = 0; i < hex.Length; i += 4)
            {
                if (i > 0) sb.Append('-');
                sb.Append(hex, i, 4);
            }
            return sb.ToString();
        }
    }

    /// <summary>Id crudo del OS (registro en Windows, /etc/machine-id en Linux).</summary>
    protected abstract string ObtenerIdCrudo();
}
