using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class ProveedorPermisosEnMemoriaTests
{
    /// <summary>
    /// Round 1 de fix (revisión adversarial post-Task 10): este fake ANTES guardaba con
    /// <c>Datos[usuarioId] = new HashSet&lt;string&gt;(permisos)</c> — deduplicaba la entrada
    /// "gratis" por el solo hecho de convertirla a HashSet. Eso NO es lo que hace
    /// PermisoUsuarioRepository real: ese repo hace un INSERT por elemento dentro de la
    /// transacción de reemplazo (foreach sin dedupe), y es el índice único
    /// (UsuarioId, Permiso) de Postgres el que rechaza el duplicado con un DbUpdateException.
    /// Un test que pasaba contra el fake viejo (`GuardarAsync_ConPermisoDuplicadoEnLaEntrada_
    /// LoDedupeaSinFallar`, ver abajo) escondió el bug real: en producción ese mismo input
    /// terminaba en un 500. Este fake ahora PRESERVA la entrada tal cual (una lista, no un
    /// HashSet) y simula la violación del índice único lanzando si hay duplicados — mismo
    /// modo de falla que el repositorio real, sin necesitar Postgres para este test unitario.
    /// </summary>
    private sealed class PermisoUsuarioRepositoryFake : IPermisoUsuarioRepository
    {
        public int LlamadasObtener { get; private set; }
        public int LlamadasReemplazar { get; private set; }
        public Dictionary<int, List<string>> Datos { get; } = new();

        public Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
        {
            LlamadasObtener++;
            var permisos = Datos.TryGetValue(usuarioId, out var p) ? p : new List<string>();
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(permisos));
        }

        public Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
        {
            LlamadasReemplazar++;

            if (permisos.Count != permisos.Distinct().Count())
                throw new InvalidOperationException(
                    "Simulación de violación del índice único (UsuarioId, Permiso): la entrada tiene duplicados.");

            Datos[usuarioId] = permisos.ToList();
            return Task.CompletedTask;
        }
    }

    // Registrar el fake como Scoped vía factory que siempre devuelve LA MISMA instancia:
    // simula fielmente el lifetime real (Scoped) mientras permite contar llamadas a través
    // de scopes distintos, igual que un singleton de test — mismo patrón usado para verificar
    // wiring de DI real en vez de mockear IServiceScopeFactory a mano.
    private static (ProveedorPermisosEnMemoria Sut, PermisoUsuarioRepositoryFake Repo) Crear()
    {
        var repo = new PermisoUsuarioRepositoryFake();
        var services = new ServiceCollection();
        services.AddScoped<IPermisoUsuarioRepository>(_ => repo);
        var provider = services.BuildServiceProvider();
        var sut = new ProveedorPermisosEnMemoria(provider.GetRequiredService<IServiceScopeFactory>());
        return (sut, repo);
    }

    /// <summary>
    /// Fake con el PRIMER SELECT bajo control manual vía TaskCompletionSource: permite forzar
    /// el interleaving exacto "arranca el SELECT, corre un GuardarAsync completo, recién
    /// entonces se libera el SELECT viejo" sin Task.Delay/Thread.Sleep. Los SELECT
    /// posteriores al primero devuelven el estado real de <see cref="Datos"/> (no la lectura
    /// congelada) -- necesario para que una relectura DESPUÉS de la invalidación (que ya no
    /// deja nada en cache, round 2 del fix) dispare un SELECT de verdad y no reciba otra vez
    /// el resultado viejo. ReemplazarPermisosAsync (usado por GuardarAsync) se resuelve de
    /// inmediato — solo el/los SELECT quedan sujetos a esta regla.
    /// </summary>
    private sealed class PermisoUsuarioRepositoryFakeControlable : IPermisoUsuarioRepository
    {
        private readonly TaskCompletionSource<IReadOnlySet<string>> _bloqueoObtener =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _primerSelectConsumido;

        public Dictionary<int, HashSet<string>> Datos { get; } = new();

        public Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
        {
            if (!_primerSelectConsumido)
            {
                _primerSelectConsumido = true;
                return _bloqueoObtener.Task;
            }

            var permisos = Datos.TryGetValue(usuarioId, out var p) ? p : new HashSet<string>();
            return Task.FromResult<IReadOnlySet<string>>(permisos);
        }

        public Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
        {
            Datos[usuarioId] = new HashSet<string>(permisos);
            return Task.CompletedTask;
        }

        /// <summary>Libera el primer SELECT bloqueado con el resultado que hubiera traído la
        /// base en el momento en que arrancó (antes de cualquier GuardarAsync posterior).</summary>
        public void LiberarObtenerCon(IReadOnlySet<string> resultado) => _bloqueoObtener.SetResult(resultado);
    }

    /// <summary>
    /// Fake para el escenario Guardar-vs-Guardar: el "commit" (escritura en <see cref="Datos"/>)
    /// sucede de forma SÍNCRONA dentro de ReemplazarPermisosAsync -- representa que la
    /// transacción contra la base ya cerró -- pero el Task que ReemplazarPermisosAsync devuelve
    /// queda pendiente hasta que el test decida completarlo. Eso separa "cuándo commiteó" de
    /// "cuándo retoma la continuación de GuardarAsync" (avanzar versión + invalidar cache),
    /// permitiendo invertir a mano el orden de las continuaciones respecto al de los commits
    /// sin Task.Delay/Thread.Sleep.
    /// </summary>
    private sealed class PermisoUsuarioRepositoryFakeConContinuacionesControlables : IPermisoUsuarioRepository
    {
        public List<TaskCompletionSource> Continuaciones { get; } = new();
        public Dictionary<int, HashSet<string>> Datos { get; } = new();

        public Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
        {
            var permisos = Datos.TryGetValue(usuarioId, out var p) ? p : new HashSet<string>();
            return Task.FromResult<IReadOnlySet<string>>(permisos);
        }

        public Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
        {
            Datos[usuarioId] = new HashSet<string>(permisos); // el commit "ya pasó"
            var continuacion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Continuaciones.Add(continuacion);
            return continuacion.Task;
        }
    }

    [Fact]
    public async Task ObtenerAsync_CacheMiss_DisparaUnSoloSelect()
    {
        var (sut, repo) = Crear();
        repo.Datos[7] = new List<string> { Permisos.VerFinanzas };

        var permisos = await sut.ObtenerAsync(7);

        Assert.Equal(1, repo.LlamadasObtener);
        Assert.Contains(Permisos.VerFinanzas, permisos);
    }

    [Fact]
    public async Task ObtenerAsync_CacheHit_NoVuelveATocarElRepositorio()
    {
        var (sut, repo) = Crear();
        repo.Datos[7] = new List<string> { Permisos.VerFinanzas };
        await sut.ObtenerAsync(7);

        await sut.ObtenerAsync(7);
        await sut.ObtenerAsync(7);

        Assert.Equal(1, repo.LlamadasObtener);
    }

    [Fact]
    public async Task ObtenerAsync_SinFilas_FailClosedDevuelveVacio()
    {
        var (sut, _) = Crear();

        var permisos = await sut.ObtenerAsync(999);

        Assert.Empty(permisos);
    }

    [Fact]
    public async Task GuardarAsync_PersisteEnElRepositorio()
    {
        var (sut, repo) = Crear();

        await sut.GuardarAsync(7, new[] { Permisos.GestionarProductos });

        Assert.Equal(1, repo.LlamadasReemplazar);
        Assert.Contains(Permisos.GestionarProductos, repo.Datos[7]);
    }

    [Fact]
    public async Task GuardarAsync_InvalidaLaCache_ElSiguienteObtenerAsyncReflejaLoNuevo()
    {
        var (sut, _) = Crear();
        await sut.ObtenerAsync(7); // puebla la cache con el estado vacío inicial

        await sut.GuardarAsync(7, new[] { Permisos.RecalcularStock });
        var releido = await sut.ObtenerAsync(7);

        Assert.Contains(Permisos.RecalcularStock, releido);
    }

    [Fact]
    public async Task ObtenerAsync_LecturaEnVueloNoPisaEscrituraMasFresca()
    {
        var repo = new PermisoUsuarioRepositoryFakeControlable();
        var services = new ServiceCollection();
        services.AddScoped<IPermisoUsuarioRepository>(_ => repo);
        var provider = services.BuildServiceProvider();
        var sut = new ProveedorPermisosEnMemoria(provider.GetRequiredService<IServiceScopeFactory>());

        // 1. Arranca un ObtenerAsync con cache-miss: dispara el SELECT y queda bloqueado
        //    esperando a que el fake decida terminarlo.
        var tareaObtenerBloqueada = sut.ObtenerAsync(7);

        // 2. Mientras el SELECT sigue en vuelo, el Admin destilda un permiso: GuardarAsync
        //    corre COMPLETO (commit + invalidación de cache) sin esperar al SELECT de arriba.
        await sut.GuardarAsync(7, new[] { Permisos.GestionarProductos });

        // 3. Recién ahora se libera el SELECT viejo, con el estado que la base tenía ANTES
        //    del guardado (vacío) -- simula que ese SELECT arrancó antes del commit.
        repo.LiberarObtenerCon(new HashSet<string>());

        // Ajuste round 2: con el fix de "invalidar en vez de escribir" (ver el test de
        // Guardar-vs-Guardar más abajo), GuardarAsync ya NO deja un valor fresco en cache para
        // que esta rama lo recupere -- solo la borra. La garantía que se sostiene ahora es más
        // angosta pero suficiente: este llamado puntual puede devolver su propia lectura
        // (posiblemente stale -- de ahí que no se assertee nada sobre ese resultado acá), pero
        // NUNCA la escribe en la cache compartida. Lo que sí tiene que valer siempre es que la
        // cache no queda corrompida: la relectura de abajo tiene que ir a buscar la verdad a la
        // base (el fake ya no tiene el SELECT congelado tras el primer llamado, ver
        // PermisoUsuarioRepositoryFakeControlable) en vez de servir el valor viejo.
        await tareaObtenerBloqueada;
        var releido = await sut.ObtenerAsync(7);
        Assert.Contains(Permisos.GestionarProductos, releido);
    }

    [Fact]
    public async Task GuardarAsync_DosGuardadosConcurrentes_ContinuacionesInvertidas_LecturaPosteriorReflejaElUltimoCommit()
    {
        var repo = new PermisoUsuarioRepositoryFakeConContinuacionesControlables();
        var services = new ServiceCollection();
        services.AddScoped<IPermisoUsuarioRepository>(_ => repo);
        var provider = services.BuildServiceProvider();
        var sut = new ProveedorPermisosEnMemoria(provider.GetRequiredService<IServiceScopeFactory>());

        // G1 guarda A: el commit contra la base ya sucedió (Datos[7] = A), pero su
        // continuación (avanzar versión + invalidar cache) queda en pausa.
        var tareaG1 = sut.GuardarAsync(7, new[] { Permisos.GestionarProductos });

        // G2 guarda B DESPUÉS de A: el commit de B pisa al de A en la base -- B es la
        // verdad vigente -- pero su continuación también queda en pausa.
        var tareaG2 = sut.GuardarAsync(7, new[] { Permisos.RecalcularStock });

        Assert.Equal(2, repo.Continuaciones.Count);

        // Invertimos el orden: la continuación de G2 (el commit más nuevo) corre PRIMERO,
        // la de G1 (el commit más viejo) corre DESPUÉS -- el peor caso posible.
        repo.Continuaciones[1].SetResult();
        await tareaG2;
        repo.Continuaciones[0].SetResult();
        await tareaG1;

        var releido = await sut.ObtenerAsync(7);

        Assert.Contains(Permisos.RecalcularStock, releido);
        Assert.DoesNotContain(Permisos.GestionarProductos, releido);
    }

    [Fact]
    public async Task GuardarAsync_ConPermisoDuplicadoEnLaEntrada_PropagaLaFallaDelRepositorio()
    {
        // Round 1 de fix (revisión adversarial post-Task 10): este test se llamaba
        // "...LoDedupeaSinFallar" y afirmaba que ProveedorPermisosEnMemoria deduplicaba en
        // silencio -- pero eso nunca fue cierto de esta capa. Pasaba solo porque el fake
        // viejo convertía la entrada a HashSet en el WRITE, un artefacto del doble que no
        // existe en PermisoUsuarioRepository real (que inserta una fila por elemento y deja
        // que el índice único de Postgres rechace el duplicado). Alineado el fake para que
        // preserve la entrada y falle igual que el repositorio real, este test pasa a
        // documentar lo contrario de lo que decía: ProveedorPermisosEnMemoria es una capa
        // "tonta" que NO deduplica -- la deduplicación real y única vive en
        // UsuarioService.GuardarPermisosAsync (`.Distinct()` antes de llegar acá), la capa
        // que efectivamente recibe input no confiable.
        var (sut, _) = Crear();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.GuardarAsync(7, new[] { Permisos.VerFinanzas, Permisos.VerFinanzas }));
    }
}
