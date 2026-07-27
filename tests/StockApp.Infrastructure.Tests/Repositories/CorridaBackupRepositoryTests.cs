using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class CorridaBackupRepositoryTests : PostgresRepositoryTestBase
{
    public CorridaBackupRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private CorridaBackupRepository Crear() => new(Context);

    [Fact]
    public async Task AgregarAsync_AsignaIdYPersiste()
    {
        var repo = Crear();
        var corrida = new CorridaBackup
        {
            IniciadaEn = new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc),
            FinalizadaEn = new DateTime(2026, 7, 27, 3, 1, 0, DateTimeKind.Utc),
            Resultado = ResultadoBackup.Exitosa,
            NombreArchivo = "backup_20260727_030000.dump",
            TamanioBytes = 1024,
        };

        var id = await repo.AgregarAsync(corrida);

        Assert.True(id > 0);
    }

    [Fact]
    public async Task ListarTodasAsync_OrdenaPorFinalizadaEnDesc()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, new DateTime(2026, 7, 26, 3, 0, 0, DateTimeKind.Utc)));
        await repo.AgregarAsync(Corrida(ResultadoBackup.Fallida, new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc)));

        var todas = await repo.ListarTodasAsync();

        Assert.Equal(2, todas.Count);
        Assert.Equal(ResultadoBackup.Fallida, todas[0].Resultado);
    }

    [Fact]
    public async Task ListarExitosasAsync_ExcluyeFallidas()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, DateTime.UtcNow));
        await repo.AgregarAsync(Corrida(ResultadoBackup.Fallida, DateTime.UtcNow));

        var exitosas = await repo.ListarExitosasAsync();

        Assert.Single(exitosas);
        Assert.Equal(ResultadoBackup.Exitosa, exitosas[0].Resultado);
    }

    [Fact]
    public async Task ObtenerUltimaExitosaAsync_SinCorridas_DevuelveNull()
    {
        var repo = Crear();

        Assert.Null(await repo.ObtenerUltimaExitosaAsync());
    }

    [Fact]
    public async Task ObtenerUltimaExitosaAsync_ConVariasExitosas_DevuelveLaMasReciente()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc), "vieja.dump"));
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), "nueva.dump"));

        var ultima = await repo.ObtenerUltimaExitosaAsync();

        Assert.Equal("nueva.dump", ultima!.NombreArchivo);
    }

    [Fact]
    public async Task EliminarAsync_BorraLaFilaFisicamente()
    {
        var repo = Crear();
        var id = await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, DateTime.UtcNow));

        await repo.EliminarAsync(id);

        Assert.Null(await repo.ObtenerPorIdAsync(id));
    }

    [Fact]
    public async Task EliminarAsync_IdInexistente_NoLanzaYNoAfectaOtrasFilas()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, DateTime.UtcNow));

        await repo.EliminarAsync(999999);

        // Assert explícito (pre-flight scan, corregido): antes este test solo probaba que NO
        // lanzaba, sin verificar ningún estado — la intención real (un id inexistente no toca
        // ninguna fila real) quedaba implícita, no escrita.
        Assert.Single(await repo.ListarTodasAsync());
    }

    private static CorridaBackup Corrida(ResultadoBackup resultado, DateTime finalizadaEn, string? nombreArchivo = null) => new()
    {
        IniciadaEn = finalizadaEn.AddMinutes(-1),
        FinalizadaEn = finalizadaEn,
        Resultado = resultado,
        NombreArchivo = resultado == ResultadoBackup.Exitosa ? (nombreArchivo ?? "backup.dump") : null,
        TamanioBytes = resultado == ResultadoBackup.Exitosa ? 1024 : null,
        MotivoFallo = resultado == ResultadoBackup.Fallida ? "pg_dump: fallo simulado" : null,
    };
}
