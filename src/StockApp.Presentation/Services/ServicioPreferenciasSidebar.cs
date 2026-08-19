using System;
using System.IO;
using System.Text.Json;

namespace StockApp.Presentation.Services;

/// <summary>
/// Clon del molde de ServicioEstadoVentana, con archivo propio: sidebar.json en vez de
/// ventana.json. Van separados porque tienen ciclos de vida distintos — el estado de ventana se
/// guarda al cerrar la app, las preferencias de sidebar cada vez que el usuario abre o cierra un
/// grupo. Mezclarlos obligaria a reescribir todo el archivo en cada click.
/// </summary>
public class ServicioPreferenciasSidebar : IServicioPreferenciasSidebar
{
    private readonly string _rutaArchivo;

    public ServicioPreferenciasSidebar() : this(RutaPorDefecto()) { }

    /// <summary>Ctor con ruta inyectable, para tests contra un path temporal.</summary>
    internal ServicioPreferenciasSidebar(string rutaArchivo) => _rutaArchivo = rutaArchivo;

    private static string RutaPorDefecto()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockApp",
            "sidebar.json");

    public PreferenciasSidebar? Cargar()
    {
        try
        {
            if (!File.Exists(_rutaArchivo)) return null;

            return JsonSerializer.Deserialize<PreferenciasSidebar>(File.ReadAllText(_rutaArchivo));
        }
        catch
        {
            // Archivo corrupto, permisos, disco: el sidebar arranca con los grupos cerrados.
            // No vale la pena romper el arranque de la app por una preferencia cosmetica.
            return null;
        }
    }

    public void Guardar(PreferenciasSidebar preferencias)
    {
        try
        {
            var carpeta = Path.GetDirectoryName(_rutaArchivo);
            if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

            File.WriteAllText(_rutaArchivo, JsonSerializer.Serialize(preferencias));
        }
        catch
        {
            // Guardar se dispara al abrir o cerrar un grupo: un fallo de IO no puede
            // interrumpir la navegacion.
        }
    }
}
