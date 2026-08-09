using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ConfiguracionAlertasRepositoryTests : PostgresRepositoryTestBase
{
    public ConfiguracionAlertasRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private ConfiguracionAlertasRepository Crear() => new(Context);

    [Fact]
    public async Task ObtenerAsync_SinFila_DevuelveConfiguracionPorDefectoDeshabilitada()
    {
        // El TRUNCATE ... CASCADE de la base de tests arrastra ConfiguracionAlertas (FK a
        // Usuarios), así que la fila sembrada por la migración NO está. El repositorio tiene
        // que tolerarlo: sin este comportamiento, todo el subsistema explota en tests.
        var repo = Crear();

        var cfg = await repo.ObtenerAsync();

        Assert.NotNull(cfg);
        Assert.Null(cfg.UrlWebhook);
        Assert.False(cfg.Habilitado);
    }

    [Fact]
    public async Task GuardarAsync_SinFilaPrevia_InsertaConId1YSePuedeReleer()
    {
        var repo = Crear();
        var cfg = await repo.ObtenerAsync();
        cfg.UrlWebhook = "https://hc-ping.com/abc";
        cfg.Habilitado = true;
        cfg.ActualizadoEn = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

        await repo.GuardarAsync(cfg);

        var releida = await new ConfiguracionAlertasRepository(Fixture.CrearContexto()).ObtenerAsync();
        Assert.Equal(1, releida.Id);
        Assert.Equal("https://hc-ping.com/abc", releida.UrlWebhook);
        Assert.True(releida.Habilitado);
    }

    [Fact]
    public async Task GuardarAsync_ConFilaPrevia_ActualizaEnLugarDeInsertarOtra()
    {
        var repo = Crear();
        var primera = await repo.ObtenerAsync();
        primera.UrlWebhook = "https://hc-ping.com/vieja";
        primera.Habilitado = true;
        await repo.GuardarAsync(primera);

        var segunda = await new ConfiguracionAlertasRepository(Fixture.CrearContexto()).ObtenerAsync();
        segunda.UrlWebhook = "https://hc-ping.com/nueva";
        segunda.Habilitado = false;
        await new ConfiguracionAlertasRepository(Fixture.CrearContexto()).GuardarAsync(segunda);

        using var ctx = Fixture.CrearContexto();
        var todas = ctx.ConfiguracionesAlertas.ToList();
        Assert.Single(todas);
        Assert.Equal("https://hc-ping.com/nueva", todas[0].UrlWebhook);
        Assert.False(todas[0].Habilitado);
    }
}
