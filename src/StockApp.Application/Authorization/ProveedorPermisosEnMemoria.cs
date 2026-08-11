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
///
/// GUARDA DE CONCURRENCIA (fix post-review, hallado por el propio implementador y confirmado
/// como bloqueante): un ObtenerAsync en cache-miss puede quedar "en vuelo" haciendo su SELECT
/// mientras un GuardarAsync concurrente para el MISMO usuario commitea y actualiza la cache. Si
/// ese SELECT viejo vuelve después y escribe sin más, pisa el valor fresco con uno stale — la
/// cache queda mintiendo hasta el próximo GuardarAsync de ese usuario, que puede no llegar
/// nunca. Se resuelve con un contador de versión MONOTÓNICO por usuario (mismo espíritu que
/// RevocadorTokensEnMemoria, que usa un "máximo gana" sobre el iat aceptado): ObtenerAsync
/// captura la versión vigente ANTES de arrancar el SELECT; si al volver la versión cambió,
/// alguien commiteó mientras tanto y la lectura se descarta (se devuelve lo que haya en cache,
/// que ya es lo fresco). GuardarAsync SIEMPRE pisa la cache sin condición — es la fuente de
/// verdad — pero primero avanza la versión, así cualquier ObtenerAsync en vuelo que revise
/// después la detecta. Nunca se sostiene un lock durante el I/O contra Postgres, y no hay lock
/// global entre usuarios: cada usuarioId tiene su propia entrada en ambos diccionarios.
/// </summary>
public sealed class ProveedorPermisosEnMemoria : IProveedorPermisos
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<int, IReadOnlySet<string>> _cache = new();
    private readonly ConcurrentDictionary<int, long> _versiones = new();

    public ProveedorPermisosEnMemoria(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId)
    {
        if (_cache.TryGetValue(usuarioId, out var enCache))
            return enCache;

        // Versión vigente ANTES de arrancar el SELECT: si un GuardarAsync concurrente para
        // este mismo usuario completa (commit + avance de versión) mientras el SELECT sigue
        // en vuelo, la versión habrá cambiado para cuando volvamos.
        var versionAntes = _versiones.GetOrAdd(usuarioId, 0);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        var permisos = await repo.ObtenerPermisosAsync(usuarioId);

        if (_versiones.GetOrAdd(usuarioId, 0) != versionAntes)
        {
            // Alguien guardó mientras esperábamos el SELECT: nuestra lectura puede ser
            // anterior a ese commit. Se descarta sin tocar la cache — GuardarAsync ya dejó
            // ahí el valor vigente — y se devuelve ESE valor en vez del que acabamos de leer,
            // para que ni siquiera el llamador de este ObtenerAsync vea el dato stale.
            return _cache.TryGetValue(usuarioId, out var fresco) ? fresco : permisos;
        }

        _cache[usuarioId] = permisos;
        return permisos;
    }

    public async Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        await repo.ReemplazarPermisosAsync(usuarioId, permisos);

        // Orden importa: primero avanza la versión, después escribe la cache — ambas líneas
        // son síncronas, sin await entre medio, así que un ObtenerAsync que revise la versión
        // en cualquier punto de esta secuencia o después la va a ver ya incrementada.
        // Invalidación = sobreescribir con el valor fresco, no un Remove(): un ObtenerAsync
        // concurrente que ya estaba en vuelo antes de este GuardarAsync no puede "revivir" un
        // valor viejo después, porque no hay ventana entre invalidar y repoblar.
        _versiones.AddOrUpdate(usuarioId, 1, (_, v) => v + 1);
        _cache[usuarioId] = new HashSet<string>(permisos);
    }
}
