using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Envuelve la escritura a disco de un export PDF, análogo a <see cref="ExportacionCsv"/>
/// (bugfix 2026-08-14): un fallo DESPUÉS de que el usuario ya eligió la ubicación en el
/// selector nativo (permiso denegado, disco lleno, ruta inválida) se informa en vez de escapar
/// de un <c>AsyncRelayCommand</c> sin observar.
/// </summary>
public static class ExportacionPdf
{
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
