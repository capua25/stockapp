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
/// GUARDAS DE CONCURRENCIA (dos rounds de review, ambos hallazgos reales):
///
/// 1) Obtener-vs-Guardar: un ObtenerAsync en cache-miss puede quedar "en vuelo" haciendo su
///    SELECT mientras un GuardarAsync concurrente para el MISMO usuario commitea. Se resuelve
///    con un contador de versión MONOTÓNICO por usuario (mismo espíritu que
///    RevocadorTokensEnMemoria, que usa un "máximo gana" sobre el iat aceptado): ObtenerAsync
///    captura la versión vigente ANTES de arrancar el SELECT; si al volver la versión cambió,
///    alguien commiteó mientras tanto y la lectura NO se cachea (se devuelve igual para ESE
///    llamado puntual, pero sin corromper la cache compartida — el próximo ObtenerAsync de ese
///    usuario vuelve a consultar la base porque la cache sigue vacía).
///
/// 2) Guardar-vs-Guardar (round 2, el fix de "versión" del punto 1 no lo cubría): dos
///    GuardarAsync concurrentes para el mismo usuario pueden tener sus continuaciones
///    post-commit reordenadas por el thread pool respecto al orden real en que commitearon
///    contra la base — nada garantiza que la continuación del commit MÁS NUEVO corra después
///    de la del commit más viejo. Escribir la cache "a ciegas" con el parámetro de cada
///    GuardarAsync (como hacía el fix del punto 1) podía dejarla con el valor de un commit
///    viejo aunque la base ya tuviera uno más nuevo. Por eso GuardarAsync NO escribe: invalida
///    (Remove) la entrada de cache tras commitear. Sea cual sea el orden en que compitan dos
///    invalidaciones, el resultado converge siempre a "sin entrada" — nunca puede haber una
///    entrada con el valor equivocado — y el próximo ObtenerAsync vuelve a consultar la base,
///    la única fuente de verdad sobre cuál commit ganó. Costo: un SELECT extra la próxima vez
///    que se consulten los permisos de ese usuario — aceptable porque guardar permisos es una
///    acción rara (un Admin, ocasional), y saca de la cache la responsabilidad de "adivinar"
///    cuál escritura es la vigente.
///
/// Nunca se sostiene un lock durante el I/O contra Postgres, y no hay lock global entre
/// usuarios: cada usuarioId tiene su propia entrada en ambos diccionarios, cada uno thread-safe
/// por clave sin lock explícito de este código (ConcurrentDictionary).
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
        // este mismo usuario completa (commit + avance de versión + invalidación) mientras el
        // SELECT sigue en vuelo, la versión habrá cambiado para cuando volvamos.
        var versionAntes = _versiones.GetOrAdd(usuarioId, 0);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        var permisos = await repo.ObtenerPermisosAsync(usuarioId);

        // Si la versión sigue igual, nadie guardó mientras esperábamos el SELECT: nuestra
        // lectura es válida y la cacheamos. Si cambió, un GuardarAsync concurrente ya
        // invalidó la cache durante nuestra lectura — la nuestra puede ser anterior a ese
        // commit, así que NO la cacheamos (resucitaría un valor viejo justo después de que
        // GuardarAsync lo invalidó a propósito). Se devuelve igual para este llamado puntual;
        // la cache queda vacía y el PRÓXIMO ObtenerAsync de este usuario vuelve a consultar
        // la base, que es la única fuente de verdad.
        if (_versiones.GetOrAdd(usuarioId, 0) == versionAntes)
            _cache[usuarioId] = permisos;

        return permisos;
    }

    public async Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        await repo.ReemplazarPermisosAsync(usuarioId, permisos);

        // Avanza la versión SIEMPRE (para que cualquier ObtenerAsync en vuelo lo detecte, ver
        // guarda 1 arriba) y después invalida — nunca escribe — la cache (ver guarda 2 arriba).
        _versiones.AddOrUpdate(usuarioId, 1, (_, v) => v + 1);
        _cache.TryRemove(usuarioId, out _);
    }
}
