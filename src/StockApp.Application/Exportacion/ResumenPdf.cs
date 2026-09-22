namespace StockApp.Application.Exportacion;

/// <summary>
/// Un total suelto del resumen que <see cref="IPdfExporter.Exportar{T}"/> imprime después de la
/// tabla principal ("Total Valor Costo", "Saldo inicial", "Saldo final", "Total general").
/// </summary>
/// <param name="Etiqueta">Rótulo del total, igual que se ve en pantalla.</param>
/// <param name="Valor">
/// El valor SIN FORMATEAR -- el mismo objeto que recibiría <see cref="ColumnaPdf"/> para una
/// celda de la tabla (<c>decimal</c>, <c>int</c>, etc.). El formato numérico (es-UY, coma
/// decimal/punto de miles) lo aplica la plantilla con el MISMO método que formatea cada celda
/// de la tabla (<c>PlantillaTabular.FormatearValor</c>) -- si el ViewModel mandara acá un
/// <c>string</c> ya formateado, habría DOS lugares formateando números del mismo documento, y
/// ya hubo un bug por justamente eso (2026-09-21, ver el comentario de
/// <c>RepartirAnchoDeColumnas</c>): dos caminos que dejan de coincidir en silencio.
/// </param>
public sealed record TotalPdf(string Etiqueta, object? Valor);

/// <summary>
/// Una mini-tabla titulada de pares etiqueta/valor debajo del resumen principal (Libro Caja:
/// "Totales por rubro", "Totales por fuente").
/// </summary>
public sealed record SeccionResumenPdf(string Titulo, IReadOnlyList<TotalPdf> Filas);

/// <summary>
/// Resumen opcional que <see cref="IPdfExporter.Exportar{T}"/> imprime DESPUÉS de la tabla
/// principal (spec de totales/resumen, 2026-09-22): totales sueltos y/o secciones tituladas.
/// El parámetro <c>resumen</c> del exportador es <c>null</c> por default a propósito -- de las 9
/// pantallas que exportan PDF, solo 3 (Valorización, Libro Caja, Reporte de tareas) tienen
/// agregados reales; las otras 6 no deben construir un resumen vacío solo para cumplir la firma.
/// </summary>
public sealed record ResumenPdf(
    IReadOnlyList<TotalPdf> Totales,
    IReadOnlyList<SeccionResumenPdf> Secciones)
{
    /// <summary>Atajo para el caso común de solo totales sueltos, sin secciones (Valorización, Reporte de tareas).</summary>
    public ResumenPdf(IReadOnlyList<TotalPdf> totales) : this(totales, [])
    {
    }
}
