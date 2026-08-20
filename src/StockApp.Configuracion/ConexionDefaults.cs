namespace StockApp.Configuracion;

/// <summary>
/// Único lugar de verdad del default de conexión cuando no hay NINGUNA config disponible
/// (ni %AppData%\GestionMunicipal\conexion.json ni appsettings.json de fábrica).
///
/// Antes de este cambio este valor estaba hardcodeado DOS veces en
/// src/StockApp.Presentation/App.axaml.cs (HttpClient principal y HttpClient "Descargas"),
/// desincronizado del default real de appsettings.json (5043). Ver
/// StockApp.Presentation.Tests.Config.ResolucionApiBaseUrlTests para el test que lo cubre.
///
/// Round 1 de esta librería unificó el LUGAR pero dejó el VALOR mal (quedó en 5000 en vez de
/// 5043): mismo bug, centralizado. Puerto real verificado en
/// src/StockApp.Api/Properties/launchSettings.json (profiles "http" y "https", ambos :5043) y
/// coincide con src/StockApp.Presentation/appsettings.json. ConexionDefaultsTests.
/// UrlPorDefecto_CoincideConElApiBaseUrlDeAppsettingsDeFabrica ata este valor con esa fuente:
/// si alguno de los dos cambia sin el otro, ese test revienta.
/// </summary>
public static class ConexionDefaults
{
    /// <summary>Clave de configuración compartida — misma forma en appsettings.json y conexion.json.</summary>
    public const string ClaveApiBaseUrl = "Api:BaseUrl";

    public const string UrlPorDefecto = "http://localhost:5043";
}
