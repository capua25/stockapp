using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Envuelve la escritura a disco de un export CSV (bugfix 2026-08-14): los 8 comandos
/// `Exportar(Csv)Async` de la app arman el CSV y delegan el guardado a
/// <see cref="IServicioGuardadoArchivo"/> sin capturar la excepción — si falla DESPUÉS de que
/// el usuario ya eligió la ubicación en el selector nativo (permiso denegado, disco lleno,
/// ruta inválida), la excepción escapaba de un <c>AsyncRelayCommand</c> sin observar: terminaba
/// muda en crash.log, sin que el operador se enterara.
///
/// No agrega mensaje de éxito: el propio selector nativo "Guardar como" del SO ya es la señal
/// de éxito del camino feliz — agregar un segundo aviso sería ruido.
///
/// Centralizado acá (mismo patrón que <see cref="RefrescoPermisos"/> para "mejor esfuerzo") en
/// vez de repetir el mismo try/catch en los 8 ViewModels: el mensaje y el criterio de qué
/// capturar son idénticos en los 8 sitios, así que un cambio futuro (ej. agregar reintento)
/// se hace en un solo lugar.
/// </summary>
public static class ExportacionCsv
{
    /// <summary>
    /// Ejecuta <paramref name="operacion"/> (armado del CSV + <see cref="IServicioGuardadoArchivo"/>)
    /// y, si falla, informa al usuario en vez de dejar que la excepción escape del comando.
    /// Captura <see cref="Exception"/> en general a propósito: a diferencia de otros catches de
    /// la app que silencian <see cref="UnauthorizedAccessException"/> por ser un 403 de la API ya
    /// avisado por el handler central de <c>App.axaml.cs</c>, acá NO hay ninguna llamada a la API
    /// — el guardado es local a disco, así que un <see cref="UnauthorizedAccessException"/> en
    /// este contexto es un permiso de FILESYSTEM denegado, exactamente uno de los fallos que esta
    /// clase existe para avisar.
    /// </summary>
    public static async Task EjecutarAsync(Func<Task> operacion, IConfirmacionService confirmacion)
    {
        try
        {
            await operacion();
        }
        catch (Exception ex)
        {
            await confirmacion.InformarAsync($"No se pudo guardar el archivo. {ex.Message}");
        }
    }
}
