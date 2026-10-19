using System;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Deriva la URL del feed de actualizaciones a partir de la URL base de la API:
/// el feed vive en <c>&lt;url de la API&gt;/updates/</c>, sin configuración propia.
/// Respeta el path base de la API (p. ej. <c>https://h/api</c> da <c>https://h/api/updates/</c>).
/// </summary>
public static class UrlFeedActualizaciones
{
    public static string Desde(string apiBaseUrl)
        => new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/"), "updates/").ToString();
}
