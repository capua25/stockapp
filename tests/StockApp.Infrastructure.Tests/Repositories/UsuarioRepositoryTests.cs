using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class UsuarioRepositoryTests : PostgresRepositoryTestBase
{
    private readonly UsuarioRepository _repo;

    public UsuarioRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new UsuarioRepository(Context);
    }

    private static Usuario NuevoUsuario(string nombre) => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "$2a$12$hash",
        Rol            = RolUsuario.Operador,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow
    };

    [Fact]
    public async Task AgregarAsync_Y_BuscarPorNombre_Roundtrip()
    {
        var usuario = NuevoUsuario("jperez");
        await _repo.AgregarAsync(usuario);

        var encontrado = await _repo.BuscarPorNombreAsync("jperez");

        Assert.NotNull(encontrado);
        Assert.Equal("jperez", encontrado!.NombreUsuario);
    }

    [Fact]
    public async Task ExisteAlgunUsuarioAsync_BdVacia_RetornaFalse()
    {
        Assert.False(await _repo.ExisteAlgunUsuarioAsync());
    }

    [Fact]
    public async Task ExisteAlgunUsuarioAsync_ConUnUsuario_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoUsuario("admin"));

        Assert.True(await _repo.ExisteAlgunUsuarioAsync());
    }

    [Fact]
    public async Task ActualizarUltimoAccesoAsync_ActualizaFechaConValorCorrecto()
    {
        var usuario = NuevoUsuario("test");
        await _repo.AgregarAsync(usuario);

        var ahora = DateTime.UtcNow;
        await _repo.ActualizarUltimoAccesoAsync(usuario.Id, ahora);

        // Fix 4: limpiar el change tracker para que la lectura vaya a la BD real
        // (ExecuteUpdateAsync no usa el change tracker, así que FindAsync podría retornar caché stale)
        Context.ChangeTracker.Clear();
        var actualizado = await Context.Usuarios.FindAsync(usuario.Id);
        Assert.NotNull(actualizado!.UltimoAcceso);
        Assert.True(actualizado.UltimoAcceso >= ahora.AddSeconds(-1),
            $"UltimoAcceso ({actualizado.UltimoAcceso}) debería ser >= ahora-1s ({ahora.AddSeconds(-1)})");
    }

    [Fact]
    public async Task ContarAdminsActivosAsync_CuentaSoloAdminsActivos()
    {
        var admin1 = new Usuario
        {
            NombreUsuario = "admin1", HashContrasena = "$2a$12$hash",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var admin2 = new Usuario
        {
            NombreUsuario = "admin2", HashContrasena = "$2a$12$hash",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var operador = NuevoUsuario("op1");

        await _repo.AgregarAsync(admin1);
        await _repo.AgregarAsync(admin2);
        await _repo.AgregarAsync(operador);

        var count = await _repo.ContarAdminsActivosAsync();

        Assert.Equal(2, count);
    }

    // ── Fix 4b: carrera de nombre duplicado (índice único IX_Usuarios_NombreUsuario) ──

    [Fact]
    public async Task AgregarAsync_NombreUsuarioDuplicado_MapeaAReglaDeNegocio()
    {
        await _repo.AgregarAsync(NuevoUsuario("jperez"));
        Context.ChangeTracker.Clear();

        // El índice único de BD es quien realmente bloquea esto; el catch de
        // UsuarioRepository lo traduce a 409 en vez de dejar pasar el 500 crudo de
        // DbUpdateException (misma carrera que el chequeo previo de UsuarioService
        // no puede cerrar solo).
        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _repo.AgregarAsync(NuevoUsuario("jperez")));
    }
}
