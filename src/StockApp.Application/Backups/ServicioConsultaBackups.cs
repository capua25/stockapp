using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Backups;

/// <summary>
/// Segunda barrera de autorización (defensa en profundidad) para la lectura de backups —
/// mismo patrón que RubroGastoService/FinanzasVistasService: _auth.Verificar ADEMÁS de la
/// policy HTTP de BackupsEndpoints. GestionarDiagnostico protege el activo más sensible del
/// sistema (un dump COMPLETO de la base), por eso esta capa no se saltea aunque el spec
/// original no la mencionara explícitamente (decisión del usuario, ver Task 6 del plan).
/// Sin interfaz — mismo criterio que ServicioLicencia/ServicioResetAdmin (naming "Servicio+Xxx"
/// del módulo de Licenciamiento: sin abstracción, inyectado como clase concreta directo en los
/// endpoints). Puramente de lectura, sin IAuditLogger (igual que FinanzasVistasService).
/// </summary>
public sealed class ServicioConsultaBackups
{
    private static readonly TimeSpan UmbralAviso = TimeSpan.FromHours(26);

    private readonly ICorridaBackupRepository _corridas;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ServicioConsultaBackups(ICorridaBackupRepository corridas, ICurrentSession session, IAuthorizationService auth)
    {
        _corridas = corridas;
        _session = session;
        _auth = auth;
    }

    public async Task<IReadOnlyList<CorridaBackupDto>> ListarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        return (await _corridas.ListarTodasAsync()).Select(ADto).ToList();
    }

    public async Task<SaludBackupDto> ObtenerSaludAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var ultima = await _corridas.ObtenerUltimaExitosaAsync();
        var vencido = ultima is null || DateTime.UtcNow - ultima.FinalizadaEn >= UmbralAviso;
        return new SaludBackupDto(ultima?.FinalizadaEn, vencido, (int)UmbralAviso.TotalHours);
    }

    /// <summary>Resuelve la ruta completa a servir. <paramref name="directorioBackups"/> lo
    /// resuelve el LLAMADOR (BackupsEndpoints, Api) vía IUserDataPathProvider.GetBackupsDirectory()
    /// — Application no puede referenciar Infrastructure.Platform (misma frontera ya resuelta
    /// así en ServicioBackup, Task 4: parámetro de método, no inyección — ver decisión de
    /// diseño 2 del Task 6).</summary>
    public async Task<(string RutaCompleta, string NombreArchivo)> ResolverArchivoParaDescargaAsync(
        int id, string directorioBackups)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var corrida = await _corridas.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"No existe la corrida de backup {id}.");
        if (corrida.Resultado != ResultadoBackup.Exitosa || corrida.NombreArchivo is null)
            throw new EntidadNoEncontradaException($"La corrida de backup {id} no tiene un archivo de backup asociado.");

        var ruta = Path.Combine(directorioBackups, corrida.NombreArchivo);
        if (!File.Exists(ruta))
            throw new EntidadNoEncontradaException($"El archivo del backup {id} no está disponible en el servidor.");

        return (ruta, corrida.NombreArchivo);
    }

    private static CorridaBackupDto ADto(CorridaBackup c) => new(
        c.Id, c.FinalizadaEn, c.Resultado.ToString(), c.NombreArchivo, c.TamanioBytes, c.MotivoFallo);
}
