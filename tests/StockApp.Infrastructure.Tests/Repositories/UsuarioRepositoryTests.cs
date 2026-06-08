using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class UsuarioRepositoryTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly UsuarioRepository _repo;

    public UsuarioRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();
        _repo = new UsuarioRepository(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

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
    public async Task ActualizarUltimoAccesoAsync_ActualizaFecha()
    {
        var usuario = NuevoUsuario("test");
        await _repo.AgregarAsync(usuario);

        var ahora = DateTime.UtcNow;
        await _repo.ActualizarUltimoAccesoAsync(usuario.Id, ahora);

        var actualizado = await _repo.ObtenerPorIdAsync(usuario.Id);
        Assert.NotNull(actualizado!.UltimoAcceso);
    }
}
