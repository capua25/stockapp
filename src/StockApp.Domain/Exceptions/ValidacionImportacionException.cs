namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando el payload de POST /finanzas/importar/confirmar (F5c) tiene referencias
/// nominales que no resuelven contra ningún maestro existente ni declarado en MaestrosNuevos,
/// o campos obligatorios del dominio ausentes. A diferencia de ReglaDeNegocioException, lleva
/// la ubicación estructurada del error (clave "Tipo[índice].Campo" → mensajes) para que F5d
/// pueda resaltar la celda exacta en la grilla de corrección.
/// </summary>
public class ValidacionImportacionException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errores { get; }

    public ValidacionImportacionException(IReadOnlyDictionary<string, string[]> errores)
        : base("El payload de confirmación de importación tiene errores de validación.")
    {
        Errores = errores;
    }
}
