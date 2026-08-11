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
}
