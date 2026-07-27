using System.Threading;

namespace StockApp.Application.Backups;

/// <summary>
/// Consumido SOLO por el desktop (implementado unicamente por BackupsApiClient) — el servidor
/// resuelve las mismas 3 operaciones via ServicioConsultaBackups (Application, Task 6), con
/// segunda barrera de autorizacion (_auth.Verificar) ademas de la policy HTTP. No hay una
/// implementacion server-side de ESTA interfaz: BackupsEndpoints inyecta ServicioConsultaBackups
/// directo, no IBackupsService — son dos tipos distintos (cliente HTTP vs. service de dominio)
/// que comparten forma pero no identidad.
///
/// Los 3 métodos aceptan CancellationToken (mismo criterio uniforme que IVelopackGateway,
/// Actualizaciones/) — ver decisión de diseño 2 del Task. DescargarAsync es el único que un
/// caller de esta entrega realmente cancela (Task 9, botón Cancelar); Listar/ObtenerSalud lo
/// exponen por consistencia de la interfaz, con default para no romper a los callers actuales.
/// </summary>
public interface IBackupsService
{
    Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default);
    Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default);
    Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default);
}
