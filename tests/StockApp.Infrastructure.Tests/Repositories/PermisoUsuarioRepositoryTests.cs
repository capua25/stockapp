using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class PermisoUsuarioRepositoryTests : PostgresRepositoryTestBase
{
    public PermisoUsuarioRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private PermisoUsuarioRepository Crear() => new(Context);

    private async Task<int> CrearOperadorAsync(string nombre)
    {
        var operador = new Usuario
        {
            NombreUsuario = nombre, HashContrasena = "hash", Rol = RolUsuario.Operador,
            Activo = true, FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();
        return operador.Id;
    }

    [Fact]
    public async Task ObtenerPermisosAsync_SinFilas_DevuelveConjuntoVacio()
    {
        var usuarioId = await CrearOperadorAsync("operador.vacio");
        var repo = Crear();

        var permisos = await repo.ObtenerPermisosAsync(usuarioId);

        Assert.Empty(permisos);
    }

    [Fact]
    public async Task ReemplazarPermisosAsync_SinFilasPrevias_InsertaTodas()
    {
        var usuarioId = await CrearOperadorAsync("operador.alta");
        var repo = Crear();

        await repo.ReemplazarPermisosAsync(usuarioId,
            new[] { Permisos.VerFinanzas, Permisos.GestionarProductos });

        var releidos = await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ObtenerPermisosAsync(usuarioId);
        Assert.Equal(2, releidos.Count);
        Assert.Contains(Permisos.VerFinanzas, releidos);
        Assert.Contains(Permisos.GestionarProductos, releidos);
    }

    [Fact]
    public async Task ReemplazarPermisosAsync_ConFilasPrevias_DejaSoloElSetNuevo()
    {
        var usuarioId = await CrearOperadorAsync("operador.reemplazo");
        var repo = Crear();
        await repo.ReemplazarPermisosAsync(usuarioId, new[] { Permisos.VerFinanzas, Permisos.GestionarTareas });

        await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ReemplazarPermisosAsync(usuarioId, new[] { Permisos.RecalcularStock });

        var final = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(usuarioId);
        Assert.Single(final);
        Assert.Contains(Permisos.RecalcularStock, final);
    }

    [Fact]
    public async Task ReemplazarPermisosAsync_ConListaVacia_DejaAlUsuarioSinPermisos()
    {
        var usuarioId = await CrearOperadorAsync("operador.destildado");
        var repo = Crear();
        await repo.ReemplazarPermisosAsync(usuarioId, new[] { Permisos.VerFinanzas });

        await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ReemplazarPermisosAsync(usuarioId, Array.Empty<string>());

        var final = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(usuarioId);
        Assert.Empty(final);
    }

    [Fact]
    public async Task DosUsuarios_TienenPermisosIndependientes()
    {
        var idA = await CrearOperadorAsync("operador.a");
        var idB = await CrearOperadorAsync("operador.b");
        var repo = Crear();

        await repo.ReemplazarPermisosAsync(idA, new[] { Permisos.VerFinanzas });
        await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ReemplazarPermisosAsync(idB, new[] { Permisos.GestionarProductos });

        var permisosA = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(idA);
        var permisosB = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(idB);
        Assert.DoesNotContain(Permisos.GestionarProductos, permisosA);
        Assert.DoesNotContain(Permisos.VerFinanzas, permisosB);
    }
}
