using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// fix/integridad-referencial: LotesImportacion existe como tabla real (FK a Usuarios por
/// UsuarioId/RevertidaPorUsuarioId, Restrict — Usuarios usa baja lógica, nunca DELETE físico).
/// Los tests de las 4 FKs desde Gastos/IngresosCaja/LineasPoa/PagosGasto hacia LotesImportacion
/// viven en AppDbContextFinanzasImportacionTests.cs.
/// </summary>
public class AppDbContextLoteImportacionTests : PostgresRepositoryTestBase
{
    public AppDbContextLoteImportacionTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<int> SembrarUsuarioAsync(string nombreUsuario)
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();
        return usuario.Id;
    }

    [Fact]
    public async Task LoteImportacion_PersisteYSePuedeConsultarPorEjercicio()
    {
        var usuarioId = await SembrarUsuarioAsync("lote-tests-1");
        var lote = new LoteImportacion
        {
            Id = Guid.NewGuid(),
            Fecha = DateTime.UtcNow,
            UsuarioId = usuarioId,
            Ejercicio = 2026,
        };
        Context.LotesImportacion.Add(lote);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrado = Context.LotesImportacion.Single(l => l.Ejercicio == 2026);
        Assert.Equal(lote.Id, encontrado.Id);
        Assert.Equal(usuarioId, encontrado.UsuarioId);
        Assert.Null(encontrado.RevertidaEn);
        Assert.Null(encontrado.RevertidaPorUsuarioId);
    }

    [Fact]
    public async Task LoteImportacion_MarcarRevertida_Persiste()
    {
        var usuarioId = await SembrarUsuarioAsync("lote-tests-2");
        var usuarioReversorId = await SembrarUsuarioAsync("lote-tests-2-reversor");
        var lote = new LoteImportacion
        {
            Id = Guid.NewGuid(),
            Fecha = DateTime.UtcNow,
            UsuarioId = usuarioId,
            Ejercicio = 2025,
        };
        Context.LotesImportacion.Add(lote);
        await Context.SaveChangesAsync();

        var fechaReversion = DateTime.UtcNow;
        lote.MarcarRevertida(fechaReversion, usuarioReversorId);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrado = Context.LotesImportacion.Single(l => l.Id == lote.Id);
        Assert.NotNull(encontrado.RevertidaEn);
        Assert.Equal(usuarioReversorId, encontrado.RevertidaPorUsuarioId);
    }

    [Fact]
    public async Task LoteImportacion_Id_NoEsGeneradoPorLaBase()
    {
        // La app genera el Guid ANTES del SaveChangesAsync (ver LoteImportacion.cs) — si el
        // mapeo EF tuviera el Id como ValueGeneratedOnAdd (default para PKs), Postgres
        // ignoraría el valor asignado y generaría uno propio (o fallaría, según el tipo). Este
        // test prueba que el Id que la app asignó ANTES de Add() es EXACTAMENTE el que persiste.
        var usuarioId = await SembrarUsuarioAsync("lote-tests-3");
        var idAsignado = Guid.NewGuid();
        Context.LotesImportacion.Add(new LoteImportacion
        {
            Id = idAsignado,
            Fecha = DateTime.UtcNow,
            UsuarioId = usuarioId,
            Ejercicio = 2023,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrado = Context.LotesImportacion.Single(l => l.Ejercicio == 2023);
        Assert.Equal(idAsignado, encontrado.Id);
    }
}
