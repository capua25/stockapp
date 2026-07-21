namespace StockApp.Application.Finanzas;

/// <summary>
/// Paso de CONFIRMACIÓN del importador (spec F5c). Verifica el permiso, valida el payload
/// COMPLETO (referencias nominales + campos obligatorios) y delega la escritura transaccional
/// en IImportacionRepository. Exige el permiso ImportarPlanillas (solo Admin, mismo que F5b).
/// </summary>
public interface IConfirmacionImportacionService
{
    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto);
    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion);
}
