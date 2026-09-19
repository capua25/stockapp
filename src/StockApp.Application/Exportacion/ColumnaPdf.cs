namespace StockApp.Application.Exportacion;

/// <summary>
/// Una columna del export PDF: de qué propiedad del DTO se lee el valor y con qué rótulo se
/// imprime el encabezado.
/// </summary>
/// <param name="Propiedad">
/// Nombre EXACTO de la propiedad pública del DTO, tal cual lo resuelve la plantilla por
/// reflexión. Conviene escribirlo con <c>nameof(...)</c> para que un rename en el DTO rompa la
/// compilación en vez de reventar en tiempo de ejecución.
/// </param>
/// <param name="Rotulo">
/// Texto del encabezado en el documento: el MISMO <c>Header</c> que muestra el DataGrid de la
/// pantalla, con sus tildes y sus espacios ("Línea POA", "% Ejecución", "Stock Ant.").
/// </param>
/// <remarks>
/// Antes el contrato era <c>IReadOnlyList&lt;string&gt;</c> con solo el nombre de la propiedad, y
/// ese nombre se imprimía crudo como encabezado: un reporte oficial de la Intendencia salía con
/// "NombreUsuario", "PorcentajeEjecucion" o "LineaPoaNombre" en la cabecera (review final,
/// Importante 3). La spec pide "exactamente las columnas que el usuario ve en pantalla": el
/// conjunto de columnas ya estaba bien, faltaban los rótulos.
///
/// Esto NO aplica al export CSV, que sigue usando sus propias constantes con nombres de
/// propiedad: un CSV se abre en una planilla y se procesa, no se archiva impreso.
/// </remarks>
public sealed record ColumnaPdf(string Propiedad, string Rotulo);
