using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de integración para AuditoriaQueryRepository.ObtenerLogAsync contra PostgreSQL real.
/// </summary>
public class AuditoriaQueryRepositoryTests : PostgresRepositoryTestBase
{
    private readonly AuditoriaQueryRepository _repo;

    public AuditoriaQueryRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new AuditoriaQueryRepository(Context);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Usuario NuevoUsuario(string nombre) => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "hash",
        Rol            = RolUsuario.Admin,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow
    };

    private LogAuditoria Log(int usuarioId, DateTime fecha, AccionAuditada accion = AccionAuditada.AltaProducto,
        string entidad = "Producto", int entidadId = 1, string detalle = "detalle") => new()
    {
        UsuarioId = usuarioId,
        Fecha     = fecha,
        Accion    = accion,
        Entidad   = entidad,
        EntidadId = entidadId,
        Detalle   = detalle
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerLogAsync_FiltraPorUsuarioId()
    {
        var ana  = NuevoUsuario("ana");
        var beto = NuevoUsuario("beto");
        Context.Usuarios.AddRange(ana, beto);
        await Context.SaveChangesAsync();

        var t = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        Context.LogsAuditoria.AddRange(
            Log(ana.Id,  t),
            Log(ana.Id,  t.AddHours(1)),
            Log(beto.Id, t.AddHours(2))
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerLogAsync(ana.Id, null, null);

        // Solo los 2 logs de ana
        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, item => Assert.Equal("ana", item.NombreUsuario));
    }

    [Fact]
    public async Task ObtenerLogAsync_FiltraPorFechas_FechaHastaFinDeDia()
    {
        var usuario = NuevoUsuario("user");
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        // Log a las 18:00hs del 2026-06-10 (debe INCLUIRSE)
        var fechaDentro = new DateTime(2026, 6, 10, 18, 0, 0, DateTimeKind.Utc);
        // Log a las 00:00hs del 2026-06-11 (debe EXCLUIRSE - día siguiente)
        var fechaFuera = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc);

        Context.LogsAuditoria.AddRange(
            Log(usuario.Id, fechaDentro, detalle: "dentro"),
            Log(usuario.Id, fechaFuera,  detalle: "fuera")
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        // FechaHasta = 2026-06-10 a medianoche → el ajuste a fin de día debe incluir
        // todos los logs del 10 (18:00hs) pero EXCLUIR el del 11 (00:00hs).
        var fechaHasta = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        var resultado = await _repo.ObtenerLogAsync(null, null, fechaHasta);

        Assert.Single(resultado);
        Assert.Equal("dentro", resultado[0].Detalle);
    }

    [Fact]
    public async Task ObtenerLogAsync_SinFiltros_RetornaAll()
    {
        var usuario = NuevoUsuario("user");
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var t = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        Context.LogsAuditoria.AddRange(
            Log(usuario.Id, t),
            Log(usuario.Id, t.AddHours(1)),
            Log(usuario.Id, t.AddHours(2))
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerLogAsync(null, null, null);

        Assert.Equal(3, resultado.Count);
    }

    [Fact]
    public async Task ObtenerLogAsync_OrdenadoPorFechaDesc()
    {
        var usuario = NuevoUsuario("user");
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var fechaVieja  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fechaMedia  = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var fechaNueva  = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Orden de inserción NO descendente (media, vieja, nueva) para que el test
        // falle si se quita el OrderByDescending.
        Context.LogsAuditoria.AddRange(
            Log(usuario.Id, fechaMedia, detalle: "media"),
            Log(usuario.Id, fechaVieja, detalle: "vieja"),
            Log(usuario.Id, fechaNueva, detalle: "nueva")
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerLogAsync(null, null, null);

        Assert.Equal(3, resultado.Count);
        // Más reciente primero
        Assert.Equal("nueva", resultado[0].Detalle);
        Assert.Equal("media", resultado[1].Detalle);
        Assert.Equal("vieja", resultado[2].Detalle);
    }
}
