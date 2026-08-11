using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class ProveedorPermisosEnMemoriaTests
{
    private sealed class PermisoUsuarioRepositoryFake : IPermisoUsuarioRepository
    {
        public int LlamadasObtener { get; private set; }
        public int LlamadasReemplazar { get; private set; }
        public Dictionary<int, HashSet<string>> Datos { get; } = new();

        public Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
        {
            LlamadasObtener++;
            var permisos = Datos.TryGetValue(usuarioId, out var p) ? p : new HashSet<string>();
            return Task.FromResult<IReadOnlySet<string>>(permisos);
        }

        public Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
        {
            LlamadasReemplazar++;
            Datos[usuarioId] = new HashSet<string>(permisos);
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
    /// Fake con el SELECT bajo control manual vía TaskCompletionSource: permite forzar el
    /// interleaving exacto "arranca el SELECT, corre un GuardarAsync completo, recién
    /// entonces se libera el SELECT viejo" sin Task.Delay/Thread.Sleep. ReemplazarPermisosAsync
    /// (usado por GuardarAsync) se resuelve de inmediato — solo el SELECT queda bloqueado.
    /// </summary>
    private sealed class PermisoUsuarioRepositoryFakeControlable : IPermisoUsuarioRepository
    {
        private readonly TaskCompletionSource<IReadOnlySet<string>> _bloqueoObtener =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Dictionary<int, HashSet<string>> Datos { get; } = new();

        public Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId) => _bloqueoObtener.Task;

        public Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
        {
            Datos[usuarioId] = new HashSet<string>(permisos);
            return Task.CompletedTask;
        }

        /// <summary>Libera el SELECT bloqueado con el resultado que hubiera traído la base
        /// en el momento en que arrancó (antes de cualquier GuardarAsync posterior).</summary>
        public void LiberarObtenerCon(IReadOnlySet<string> resultado) => _bloqueoObtener.SetResult(resultado);
    }

    [Fact]
    public async Task ObtenerAsync_CacheMiss_DisparaUnSoloSelect()
    {
        var (sut, repo) = Crear();
        repo.Datos[7] = new HashSet<string> { Permisos.VerFinanzas };

        var permisos = await sut.ObtenerAsync(7);

        Assert.Equal(1, repo.LlamadasObtener);
        Assert.Contains(Permisos.VerFinanzas, permisos);
    }

    [Fact]
    public async Task ObtenerAsync_CacheHit_NoVuelveATocarElRepositorio()
    {
        var (sut, repo) = Crear();
        repo.Datos[7] = new HashSet<string> { Permisos.VerFinanzas };
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
        var resultadoLecturaVieja = await tareaObtenerBloqueada;

        // La lectura vieja, aunque terminó después, no puede haber pisado lo que GuardarAsync
        // ya dejó -- ni en lo que devuelve, ni en lo que queda cacheado para el próximo pedido.
        Assert.Contains(Permisos.GestionarProductos, resultadoLecturaVieja);

        var releido = await sut.ObtenerAsync(7);
        Assert.Contains(Permisos.GestionarProductos, releido);
    }

    [Fact]
    public async Task GuardarAsync_ConPermisoDuplicadoEnLaEntrada_LoDedupeaSinFallar()
    {
        var (sut, _) = Crear();

        await sut.GuardarAsync(7, new[] { Permisos.VerFinanzas, Permisos.VerFinanzas });
        var permisos = await sut.ObtenerAsync(7);

        Assert.Single(permisos);
        Assert.Contains(Permisos.VerFinanzas, permisos);
    }
}
