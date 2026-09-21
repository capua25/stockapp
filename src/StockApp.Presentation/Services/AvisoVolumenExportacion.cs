using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Aviso de volumen antes de exportar a PDF (spec 2026-09-18): sin paginación en el sistema
/// (decisión documentada en DocumentoAdministrativoRepository.cs:42-46), exportar una tabla sin
/// filtro de fecha dentro de unos años puede ser miles de páginas, y alguien le va a dar
/// imprimir. Se avisa y se pide confirmación por encima de <see cref="UmbralFilas"/>.
/// </summary>
public static class AvisoVolumenExportacion
{
    public const int UmbralFilas = 500;
    private const int FilasPorPaginaEstimadas = 40;

    /// <summary>
    /// Si <paramref name="cantidadFilas"/> supera <see cref="UmbralFilas"/>, pregunta al
    /// usuario confirmando la cantidad aproximada de páginas. Si no lo supera, confirma
    /// automáticamente sin interrumpir (caso común).
    /// </summary>
    public static async Task<bool> ConfirmarAsync(int cantidadFilas, IConfirmacionService confirmacion)
    {
        if (cantidadFilas <= UmbralFilas)
            return true;

        var paginasEstimadas = (int)Math.Ceiling(cantidadFilas / (double)FilasPorPaginaEstimadas);
        return await confirmacion.PreguntarAsync(
            $"Esta exportación tiene {cantidadFilas} filas y va a generar aproximadamente " +
            $"{paginasEstimadas} páginas. ¿Confirma continuar?");
    }
}
