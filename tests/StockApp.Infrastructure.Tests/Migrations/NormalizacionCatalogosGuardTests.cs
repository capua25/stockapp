using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Migrations;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Migrations;

/// <summary>
/// Prueba el guardián SQL de la migración AgregaIndiceFuncionalNombreCatalogos
/// (NormalizacionCatalogosSql.GuardiaColisionesDeNombre) directamente contra Postgres real.
///
/// Por qué NO se prueba corriendo la migración real de punta a punta (levantando un
/// contenedor sin migrar hasta el punto anterior a esta migración, insertando los
/// duplicados con el índice viejo case-sensitive, y recién ahí migrando): eso requeriría
/// un SEGUNDO contenedor Testcontainers activo en simultáneo con el de PostgresFixture
/// (que retiene LockInicializacionContenedores durante TODA la vida de la collection).
/// Pedir ese lock una segunda vez desde el mismo proceso mientras el primero sigue vivo
/// no es solo un deadlock práctico (el timeout de 5 minutos del lock espera a que el
/// propio fixture que lo pidió se libere, cosa que no pasa hasta que termina toda la
/// collection) — es exactamente la condición de carrera de Testcontainers/Ryuk que ese
/// lock existe para evitar (ver el comentario completo en LockInicializacionContenedores).
///
/// En cambio, esta clase ejecuta el MISMO texto SQL que corre dentro de la migración real
/// (constante compartida — ver NormalizacionCatalogosSql) contra el container YA migrado
/// de la collection, dentro de una transacción que nunca se commitea: dropea el índice
/// funcional de la tabla bajo prueba (DDL es transaccional en Postgres), inserta dos filas
/// que colisionan solo por casing, corre el guardián y verifica el mensaje de abort — todo
/// eso se revierte solo al hacer ROLLBACK, sin dejar rastro para otros tests de la misma
/// collection ni para los del resto de la suite.
/// </summary>
public class NormalizacionCatalogosGuardTests : PostgresRepositoryTestBase
{
    public NormalizacionCatalogosGuardTests(PostgresFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Guardia_DosCategoriasColisionanPorCasing_AbortaConMensajeDetallado()
    {
        await using var tx = await Context.Database.BeginTransactionAsync();
        try
        {
            await Context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Categorias_Nombre\";");

            var cat1 = new Categoria { Nombre = "Centro" };
            var cat2 = new Categoria { Nombre = "centro" };
            Context.Categorias.AddRange(cat1, cat2);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            var ex = await Assert.ThrowsAsync<PostgresException>(
                () => Context.Database.ExecuteSqlRawAsync(NormalizacionCatalogosSql.GuardiaColisionesDeNombre));

            Assert.Contains("ABORTADA", ex.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Categorias", ex.MessageText, StringComparison.Ordinal);
            Assert.Contains("centro", ex.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(cat1.Id.ToString(), ex.MessageText, StringComparison.Ordinal);
            Assert.Contains(cat2.Id.ToString(), ex.MessageText, StringComparison.Ordinal);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Guardia_DosZonasColisionanPorCasing_AbortaConMensajeDetallado()
    {
        await using var tx = await Context.Database.BeginTransactionAsync();
        try
        {
            await Context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Zonas_Nombre\";");

            var z1 = new Zona { Nombre = "Norte" };
            var z2 = new Zona { Nombre = "NORTE" };
            Context.Zonas.AddRange(z1, z2);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();

            var ex = await Assert.ThrowsAsync<PostgresException>(
                () => Context.Database.ExecuteSqlRawAsync(NormalizacionCatalogosSql.GuardiaColisionesDeNombre));

            Assert.Contains("Zonas", ex.MessageText, StringComparison.Ordinal);
            Assert.Contains("norte", ex.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(z1.Id.ToString(), ex.MessageText, StringComparison.Ordinal);
            Assert.Contains(z2.Id.ToString(), ex.MessageText, StringComparison.Ordinal);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Guardia_SinColisiones_NoLanza()
    {
        // Mismo camino que corre en producción cuando los 7 catálogos ya están limpios
        // (o para los 4 catálogos nuevos, que arrancan vacíos): el guardián no debe
        // encontrar nada que reportar y la migración sigue de largo.
        await using var tx = await Context.Database.BeginTransactionAsync();
        try
        {
            var ex = await Record.ExceptionAsync(
                () => Context.Database.ExecuteSqlRawAsync(NormalizacionCatalogosSql.GuardiaColisionesDeNombre));

            Assert.Null(ex);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
