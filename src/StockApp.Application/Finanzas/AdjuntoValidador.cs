using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Fuente ÚNICA de validación de adjuntos: whitelist de tipo por magic bytes (no por
/// extensión, que se puede falsear) y tope de tamaño. Compartida entre AdjuntoService
/// (API) y el desktop (ServicioSeleccionArchivo) — spec F3 decisión 7: no duplicar el
/// hardcode de firmas entre capas.
/// </summary>
public static class AdjuntoValidador
{
    public const long TamanoMaximoBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly IReadOnlyList<string> ContentTypesPermitidos =
        new[] { "application/pdf", "image/jpeg", "image/png" };

    private static readonly byte[] MagicPdf = { 0x25, 0x50, 0x44, 0x46 };             // %PDF
    private static readonly byte[] MagicJpg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] MagicPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Devuelve el content-type real según los primeros bytes, o null si no matchea la whitelist.</summary>
    public static string? DetectarContentType(byte[] contenido)
    {
        if (EmpiezaCon(contenido, MagicPdf)) return "application/pdf";
        if (EmpiezaCon(contenido, MagicJpg)) return "image/jpeg";
        if (EmpiezaCon(contenido, MagicPng)) return "image/png";
        return null;
    }

    private static bool EmpiezaCon(byte[] contenido, byte[] firma)
    {
        if (contenido.Length < firma.Length)
            return false;

        for (var i = 0; i < firma.Length; i++)
            if (contenido[i] != firma[i])
                return false;

        return true;
    }

    /// <summary>Valida tamaño y MIME real (por magic bytes). Lanza ReglaDeNegocioException con mensaje claro.</summary>
    public static void Validar(byte[] contenido, string nombreArchivo)
    {
        if (contenido.LongLength > TamanoMaximoBytes)
            throw new ReglaDeNegocioException(
                $"El archivo '{nombreArchivo}' supera el tamaño máximo permitido de 10 MB.");

        if (DetectarContentType(contenido) is null)
            throw new ReglaDeNegocioException(
                $"El archivo '{nombreArchivo}' no es un tipo permitido. Solo se aceptan PDF, JPG o PNG.");
    }
}
