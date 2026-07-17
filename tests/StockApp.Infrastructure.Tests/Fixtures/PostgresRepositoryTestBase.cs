using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Fixtures;

/// <summary>
/// Base para tests de repositorio contra Postgres real. Antes de cada test hace TRUNCATE
/// de todas las tablas con RESTART IDENTITY para aislar el estado y resetear las identidades.
/// </summary>
[Collection("Postgres")]
public abstract class PostgresRepositoryTestBase : IDisposable
{
    protected readonly PostgresFixture Fixture;
    protected readonly AppDbContext Context;

    protected PostgresRepositoryTestBase(PostgresFixture fixture)
    {
        Fixture = fixture;
        LimpiarTablas();
        Context = fixture.CrearContexto();
    }

    public void Dispose() => Context.Dispose();

    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\" " +
            "RESTART IDENTITY CASCADE;");
    }
}
