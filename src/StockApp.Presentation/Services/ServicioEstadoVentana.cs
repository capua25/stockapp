using System;
using System.IO;
using System.Text.Json;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioEstadoVentana"/>. Persiste el estado en un
/// archivo JSON local por PC (no por usuario logueado, no en BD/API), en
/// <c>%APPDATA%/StockApp/ventana.json</c> en Windows o <c>~/.config/StockApp/ventana.json</c>
/// en Linux (resuelto por <see cref="Environment.SpecialFolder.ApplicationData"/>).
/// Robusta ante archivo corrupto/ilegible (devuelve <c>null</c>) y ante errores de IO al
/// guardar (no propaga la excepción: cerrar la app nunca debe fallar por esto).
/// </summary>
public class ServicioEstadoVentana : IServicioEstadoVentana
{
    private readonly string _rutaArchivo;

    public ServicioEstadoVentana() : this(RutaPorDefecto())
    {
    }

    /// <summary>
    /// Constructor con ruta inyectable, para tests (round-trip con un path temporal).
    /// </summary>
    internal ServicioEstadoVentana(string rutaArchivo)
    {
        _rutaArchivo = rutaArchivo;
    }

    private static string RutaPorDefecto()
    {
        var carpeta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockApp");

        return Path.Combine(carpeta, "ventana.json");
    }

    /// <inheritdoc />
    public EstadoVentana? Cargar()
    {
        try
        {
            if (!File.Exists(_rutaArchivo))
                return null;

            var json = File.ReadAllText(_rutaArchivo);
            return JsonSerializer.Deserialize<EstadoVentana>(json);
        }
        catch
        {
            // Archivo corrupto/ilegible (JSON inválido, permisos, etc.): se ignora y la
            // ventana principal abre con sus defaults.
            return null;
        }
    }

    /// <inheritdoc />
    public void Guardar(EstadoVentana estado)
    {
        try
        {
            var carpeta = Path.GetDirectoryName(_rutaArchivo);
            if (!string.IsNullOrEmpty(carpeta))
                Directory.CreateDirectory(carpeta);

            var json = JsonSerializer.Serialize(estado);
            File.WriteAllText(_rutaArchivo, json);
        }
        catch
        {
            // Errores de IO al guardar (disco lleno, permisos, etc.) no deben tirar la app
            // al cerrar.
        }
    }
}
