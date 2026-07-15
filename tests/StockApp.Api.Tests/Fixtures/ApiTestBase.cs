using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Licenciamiento;
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

        // Cada test arranca con la licencia ACTIVA (algunos la bloquean explícitamente).
        // El EstadoLicencia es singleton y se comparte en la collection: restaurarlo evita
        // que un test de modo-bloqueado filtre estado a los demás.
        Factory.Services.GetRequiredService<EstadoLicencia>().Activada = true;
    }

    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\" RESTART IDENTITY CASCADE;");
    }
}
