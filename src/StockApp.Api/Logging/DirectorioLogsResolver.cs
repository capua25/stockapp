using Microsoft.Extensions.Configuration;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Logging;

/// <summary>
/// Unica fuente de verdad para el directorio de logs (Entrega 2, fix de review: "dos fuentes
/// de verdad"). Antes, el arranque de Serilog en Program.cs resolvia
/// Configuration["Logs:Directorio"] con fallback a IUserDataPathProvider, pero LogsEndpoints
/// leia siempre IUserDataPathProvider sin mirar la config -- si alguien seteaba Logs:Directorio
/// en produccion para mover los logs de disco, Serilog escribia en un directorio y el endpoint
/// leia otro, y el desktop diria "sin logs" para siempre mientras los logs se acumulaban
/// invisibles. Ambos lados llaman ahora a este mismo metodo.
/// </summary>
public static class DirectorioLogsResolver
{
    public static string Resolver(IConfiguration configuration, IUserDataPathProvider paths)
    {
        var directorioConfigurado = configuration["Logs:Directorio"];
        return string.IsNullOrWhiteSpace(directorioConfigurado)
            ? paths.GetLogsDirectory()
            : directorioConfigurado;
    }
}
