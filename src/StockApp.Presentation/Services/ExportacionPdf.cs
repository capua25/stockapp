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

    /// <summary>
    /// Ofrece abrir el PDF recién guardado con el visor del sistema, reusando
    /// <see cref="IServicioAperturaArchivo"/> (ya usado para adjuntos): escribe a un temporal
    /// propio y dispara <c>Process.Start(UseShellExecute = true)</c> -- desde el visor el
    /// usuario obtiene la ventana de impresión completa del SO (Avalonia no tiene diálogo de
    /// impresión propio, ver AvaloniaUI/Avalonia#12567).
    /// Recibe <paramref name="seGuardo"/> -- el resultado de <c>GuardarBytesAsync</c> -- como
    /// parámetro obligatorio en vez de que el llamador decida si invocar o no este método:
    /// con nueve pantallas (Tareas 12-20) consumiendo esta firma, dejar el chequeo del lado
    /// del llamador significa nueve oportunidades de olvidarlo. Si <paramref name="seGuardo"/>
    /// es <c>false</c> (usuario canceló el selector de archivo), no pregunta ni abre nada.
    /// Un fallo al ABRIR (por ejemplo, sin visor de PDF asociado) se informa con un mensaje
    /// distinto al de fallo de guardado/exportación: el archivo ya está guardado en disco,
    /// así que no se le puede mentir al usuario diciéndole que la exportación falló.
    /// </summary>
    public static async Task OfrecerAbrirAsync(
        bool seGuardo, byte[] contenido, string nombreArchivo,
        IConfirmacionService confirmacion, IServicioAperturaArchivo apertura)
    {
        if (!seGuardo)
            return;

        var abrir = await confirmacion.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?");
        if (!abrir)
            return;

        try
        {
            await apertura.AbrirAsync(nombreArchivo, contenido);
        }
        catch (Exception ex)
        {
            await confirmacion.InformarAsync(
                $"El PDF se guardó correctamente, pero no se pudo abrir automáticamente. {ex.Message}");
        }
    }
}
