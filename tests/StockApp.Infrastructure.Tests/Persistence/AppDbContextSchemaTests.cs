using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class AppDbContextSchemaTests
{
    private readonly PostgresFixture _fixture;

    public AppDbContextSchemaTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public void AppDbContext_ExponeTodosLosDbSet()
    {
        using var ctx = _fixture.CrearContexto();
        Assert.NotNull(ctx.Usuarios);
        Assert.NotNull(ctx.Productos);
        Assert.NotNull(ctx.Categorias);
        Assert.NotNull(ctx.Proveedores);
        Assert.NotNull(ctx.UnidadesMedida);
        Assert.NotNull(ctx.MovimientosStock);
        Assert.NotNull(ctx.LogsAuditoria);
    }
}
