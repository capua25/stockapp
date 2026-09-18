using System;
using Microsoft.Extensions.Configuration;

namespace StockApp.Configuracion;

/// <summary>
/// Precedencia de resolución de configuración (2026-08-20, configurador de conexión;
/// centralizada acá 2026-09-18 — ver bug de origen abajo):
/// 1. %AppData%\GestionMunicipal\conexion.json — lo que escribe tools/StockApp.Configurador.
/// 2. appsettings.json del directorio de instalación — valor de fábrica.
/// 3. ConexionDefaults.UrlPorDefecto — único fallback hardcodeado.
///
/// Los providers de Microsoft.Extensions.Configuration.Json se aplican en orden: el que se
/// agrega DESPUÉS gana. Por eso conexion.json se agrega después de appsettings.json. Ambos son
/// optional: true — si faltan, ResolverApiBaseUrl cae al único default.
///
/// Bug de origen (2026-09-18): esta cadena vivía completa en App.axaml.cs
/// (StockApp.Presentation), pero tools/StockApp.Configurador solo leía conexion.json
/// (ConexionConfigStore.Leer) y caía directo al default si faltaba — se saltaba el nivel del
/// medio. En una instalación fresca (sin conexion.json) el Configurador mostraba
/// localhost:5043 mientras la app real usaba la URL de appsettings.json. Ahora ambos binarios
/// llaman a ESTA clase; App.axaml.cs y ConfiguradorViewModel no vuelven a implementar la
/// cadena por su cuenta.
///
/// Los parámetros de override existen solo para poder testear la precedencia sin depender de
/// AppContext.BaseDirectory ni de %AppData% reales; en producción se llaman sin argumentos (o,
/// en el caso del Configurador, con el override de ruta de conexion.json que ya usaba para
/// poder testear sin tocar el %AppData% real de la máquina).
/// </summary>
public static class ResolucionConexion
{
    /// <summary>
    /// Arma el <see cref="IConfiguration"/> con las dos capas de archivo (appsettings.json,
    /// conexion.json), en ese orden — conexion.json gana por agregarse después.
    /// </summary>
    public static IConfiguration ConstruirConfiguracion(
        string? rutaAppsettingsOverride = null,
        string? rutaConexionOverride = null)
    {
        var builder = new ConfigurationBuilder();

        if (rutaAppsettingsOverride is null)
        {
            builder.SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        }
        else
        {
            builder.AddJsonFile(rutaAppsettingsOverride, optional: true, reloadOnChange: false);
        }

        var rutaConexion = rutaConexionOverride ?? RutaConexion.ObtenerRutaArchivo();
        builder.AddJsonFile(rutaConexion, optional: true, reloadOnChange: false);

        return builder.Build();
    }

    /// <summary>ÚNICO lugar que resuelve Api:BaseUrl a partir de un <see cref="IConfiguration"/> ya armado.</summary>
    public static string ResolverApiBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration[ConexionDefaults.ClaveApiBaseUrl];
        return string.IsNullOrWhiteSpace(baseUrl) ? ConexionDefaults.UrlPorDefecto : baseUrl;
    }

    /// <summary>
    /// Atajo: arma la configuración y resuelve en un solo paso. Pensado para consumidores que
    /// no necesitan retener el <see cref="IConfiguration"/> intermedio (tools/StockApp.Configurador).
    /// </summary>
    public static string ResolverUrlInicial(
        string? rutaAppsettingsOverride = null,
        string? rutaConexionOverride = null)
    {
        var configuration = ConstruirConfiguracion(rutaAppsettingsOverride, rutaConexionOverride);
        return ResolverApiBaseUrl(configuration);
    }
}
