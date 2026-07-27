using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ICorridaBackupRepository
{
    Task<int> AgregarAsync(CorridaBackup corrida);

    /// <summary>Todas las corridas (Exitosa y Fallida), ordenadas por FinalizadaEn desc.</summary>
    Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync();

    /// <summary>Solo Resultado == Exitosa, ordenadas por FinalizadaEn desc. Input de PoliticaRetencion.</summary>
    Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync();

    Task<CorridaBackup?> ObtenerPorIdAsync(int id);

    /// <summary>La corrida Exitosa más reciente por FinalizadaEn, o null si nunca hubo una. Usado
    /// por el endpoint de salud (Task 6), el banner de InicioViewModel (Task 11) y el catch-up
    /// al arrancar BackupProgramadoService (Task 5).</summary>
    Task<CorridaBackup?> ObtenerUltimaExitosaAsync();

    /// <summary>Baja FÍSICA (no lógica): usada por la retención (Task 4) para descartar corridas
    /// viejas junto con su archivo en disco — a diferencia del resto del dominio, no tiene
    /// sentido conservar el metadato de un backup cuyo archivo ya no existe.</summary>
    Task EliminarAsync(int id);
}
