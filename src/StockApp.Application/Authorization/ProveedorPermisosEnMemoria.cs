using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación SINGLETON en memoria de proceso (mismo criterio que RevocadorTokensEnMemoria):
/// ConcurrentDictionary como cache, IPermisoUsuarioRepository (Scoped, usa AppDbContext) para el
/// SELECT/reemplazo en cache-miss o en GuardarAsync. Un Singleton no puede inyectar un Scoped por
/// constructor sin crear una dependencia cautiva (el mismo AppDbContext/conexión quedaría vivo
/// para siempre) — por eso se inyecta IServiceScopeFactory y se crea un scope propio por
/// operación, mismo patrón que BackupProgramadoService/DisparadorBackupManual ya usan en este
/// repo para el mismo problema de lifetime.
///
/// LIMITACIÓN ACEPTADA (spec, "Limitación conocida"): la cache es por PROCESO. Con más de una
/// instancia de API corriendo en paralelo, un cambio de permisos guardado en una no invalida la
/// cache de las otras. Hoy corre una sola instancia bajo systemd — misma limitación ya aceptada
/// en RevocadorTokensEnMemoria.
/// </summary>
public sealed class ProveedorPermisosEnMemoria : IProveedorPermisos
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<int, IReadOnlySet<string>> _cache = new();

    public ProveedorPermisosEnMemoria(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId)
    {
        if (_cache.TryGetValue(usuarioId, out var enCache))
            return enCache;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        var permisos = await repo.ObtenerPermisosAsync(usuarioId);

        _cache[usuarioId] = permisos;
        return permisos;
    }

    public async Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        await repo.ReemplazarPermisosAsync(usuarioId, permisos);

        // Invalidación = sobreescribir con el valor fresco, no un Remove(): un ObtenerAsync
        // concurrente que ya estaba en vuelo antes de este GuardarAsync no puede "revivir" un
        // valor viejo después, porque no hay ventana entre invalidar y repoblar.
        _cache[usuarioId] = new HashSet<string>(permisos);
    }
}
