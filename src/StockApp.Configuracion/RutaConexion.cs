using System;
using System.IO;

namespace StockApp.Configuracion;

/// <summary>
/// Ruta del archivo que guarda la URL del servidor API configurada por
/// tools/StockApp.Configurador y leída por StockApp.Presentation.
///
/// Vive en %AppData%\GestionMunicipal\conexion.json (Environment.SpecialFolder.ApplicationData;
/// en Linux resuelve a ~/.config/GestionMunicipal/conexion.json) y NO en el directorio de
/// instalación: Velopack reemplaza esa carpeta entera en cada actualización, así que cualquier
/// config que viviera ahí se perdería en el próximo update (mismo motivo por el que
/// ventana.json y sidebar.json ya viven fuera).
/// </summary>
public static class RutaConexion
{
    public const string NombreCarpeta = "GestionMunicipal";
    public const string NombreArchivo = "conexion.json";

    /// <summary>Ruta real, bajo %AppData% (o ~/.config en Linux) del usuario actual.</summary>
    public static string ObtenerRutaArchivo() =>
        ObtenerRutaArchivo(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// Overload testeable: recibe la carpeta base explícita en vez de resolverla desde
    /// Environment.SpecialFolder.ApplicationData, para no depender del entorno real en tests.
    /// </summary>
    public static string ObtenerRutaArchivo(string carpetaBase) =>
        Path.Combine(carpetaBase, NombreCarpeta, NombreArchivo);
}
