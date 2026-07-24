namespace StockApp.Application.Finanzas;

/// <summary>
/// Contrato único del cliente de escritorio contra los 4 endpoints del importador (F5b/F5c
/// análisis+confirmación+reversa, F5d historial). A diferencia del servidor (IAnalisisImportacionService
/// + IConfirmacionImportacionService separados), acá se unifica en UNA interfaz porque el
/// wizard de la UI consume las 4 operaciones desde el mismo flujo.
/// </summary>
public interface IImportacionService
{
    Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds,
        string nombreArchivoPoa, byte[] poaOds,
        int ejercicio);

    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto);

    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion);

    Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync();
}
