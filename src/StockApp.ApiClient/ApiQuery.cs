// src/StockApp.ApiClient/ApiQuery.cs
namespace StockApp.ApiClient;

/// <summary>
/// Armado de query strings para los GET con filtros (/productos, /movimientos/historial,
/// /reportes/*, /auditoria): omite parámetros nulos y escapa los valores.
/// </summary>
internal static class ApiQuery
{
    internal static string Construir(params (string Clave, string? Valor)[] parametros)
    {
        var partes = parametros
            .Where(p => p.Valor is not null)
            .Select(p => $"{p.Clave}={Uri.EscapeDataString(p.Valor!)}")
            .ToList();

        return partes.Count == 0 ? string.Empty : "?" + string.Join("&", partes);
    }

    /// <summary>Formato round-trip "O": lo parsea el binding DateTime de Minimal APIs sin pérdida.</summary>
    internal static string? Fecha(DateTime? fecha) => fecha?.ToString("O");
}
