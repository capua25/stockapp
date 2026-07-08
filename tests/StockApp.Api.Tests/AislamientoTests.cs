using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Prueba explícita de que el AppDbContext resuelto por DI dentro de StockApp.Api
/// (Program.cs) apunta al Postgres de Testcontainers de ApiFactory, y NO al Postgres
/// local de desarrollo (localhost:5432). Ver nota al pie del plan de Fase 2a: la
/// lectura eager de configuración pre-Build() rompía este aislamiento silenciosamente
/// (ConnectionStrings:Default caía al fallback de appsettings.json sin que ningún test
/// lo detectara, porque ese fallback también resolvía a un Postgres real corriendo en
/// la máquina).
/// </summary>
public class AislamientoTests : ApiTestBase
{
    public AislamientoTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public void AppDbContext_ResueltoPorDI_ApuntaAlContenedorDeTest_NoAlPostgresLocal()
    {
        using var scope = Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connectionStringEfectiva = ctx.Database.GetConnectionString();

        Assert.Equal(Factory.ConnectionStringDelContenedor, connectionStringEfectiva);
        Assert.DoesNotContain("Port=5432", connectionStringEfectiva, StringComparison.OrdinalIgnoreCase);
    }
}
