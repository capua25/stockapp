using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
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

    /// <summary>
    /// Fix (review adversarial, IMPORTANTE 4): la versión anterior de este test insertaba un
    /// LoteImportacion con un Id asignado y comprobaba que el Id leído era el mismo — pero eso es
    /// tautológico para una PK Guid en Npgsql. Sin ValueGeneratedNever(), EF usaría
    /// GuidValueGenerator del lado CLIENTE, que sólo actúa si la propiedad tiene el default CLR
    /// (Guid.Empty) — un Id ya asignado por la app (como hace ConfirmarAsync) nunca dispara ese
    /// generador, así que el test viejo pasaba exactamente igual con o sin la config (verificado
    /// por mutación: borrar ValueGeneratedNever() de AppDbContext.cs y correrlo seguía en verde).
    /// Este test ahora asegura la config real contra el METAMODELO de EF en vez de un
    /// comportamiento indistinguible en runtime.
    /// </summary>
    [Fact]
    public void LoteImportacion_Id_EstaConfiguradoComoValueGeneratedNever()
    {
        var propiedadId = Context.Model.FindEntityType(typeof(LoteImportacion))!.FindProperty("Id")!;

        Assert.Equal(ValueGenerated.Never, propiedadId.ValueGenerated);
    }

    /// <summary>
    /// Minor 8 del review adversarial: ValueGeneratedNever() deja pasar Guid.Empty como PK sin
    /// ningún guard propio -- no alcanzable HOY porque ConfirmarAsync siempre usa Guid.NewGuid()
    /// (que nunca produce Guid.Empty), pero este test fija el comportamiento actual: un segundo
    /// insert con el mismo Id (Guid.Empty u otro cualquiera) da 23505 sobre PK_LotesImportacion,
    /// que ConfirmarAsync (Repositories/ImportacionRepository.cs) ya traduce genéricamente a
    /// ReglaDeNegocioException vía ObtenerRestriccionUnicaViolada -- nunca un 500 pelado, aunque
    /// el mensaje resultante no distinga "colisión de Id" de cualquier otra violación única.
    /// </summary>
    [Fact]
    public async Task LoteImportacion_IdDuplicado_TiraViolacionDeClavePrimaria()
    {
        var usuarioId = await SembrarUsuarioAsync("lote-tests-4");
        var idDuplicado = Guid.Empty;
        Context.LotesImportacion.Add(new LoteImportacion
        {
            Id = idDuplicado, Fecha = DateTime.UtcNow, UsuarioId = usuarioId, Ejercicio = 2020,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.LotesImportacion.Add(new LoteImportacion
        {
            Id = idDuplicado, Fecha = DateTime.UtcNow, UsuarioId = usuarioId, Ejercicio = 2021,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("PK_LotesImportacion", pg.ConstraintName);
    }
}
