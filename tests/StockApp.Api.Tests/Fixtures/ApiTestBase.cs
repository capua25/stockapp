using Microsoft.EntityFrameworkCore;
using Xunit;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Base para tests de integración de StockApp.Api. Antes de cada test hace TRUNCATE
/// de todas las tablas con RESTART IDENTITY para aislar el estado — mismo patrón que
/// PostgresRepositoryTestBase en StockApp.Infrastructure.Tests (Fase 1).
/// </summary>
[Collection("Api")]
public abstract class ApiTestBase
{
    protected readonly ApiFactory Factory;

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        LimpiarTablas();
    }

    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\" RESTART IDENTITY CASCADE;");
    }
}
