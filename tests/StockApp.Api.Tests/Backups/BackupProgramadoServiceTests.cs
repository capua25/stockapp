using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Api.Backups;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Api.Tests.Backups;

public class BackupProgramadoServiceTests
{
    private sealed class CorridaBackupRepositoryEspiaFake : ICorridaBackupRepository
    {
        private readonly List<object> _instanciasQueAgregaron;
        public CorridaBackup? UltimaExitosa { get; set; }

        public CorridaBackupRepositoryEspiaFake(List<object> instanciasQueAgregaron)
            => _instanciasQueAgregaron = instanciasQueAgregaron;

        public Task<int> AgregarAsync(CorridaBackup corrida)
        {
            _instanciasQueAgregaron.Add(this);
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());

        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());

        public Task<CorridaBackup?> ObtenerPorIdAsync(int id) => Task.FromResult<CorridaBackup?>(null);
        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync() => Task.FromResult(UltimaExitosa);
        public Task EliminarAsync(int id) => Task.CompletedTask;
    }

    private sealed class EjecutorPgDumpFake : IEjecutorPgDump
    {
        public Task<ResultadoEjecucionPgDump> EjecutarAsync(string c, string r, CancellationToken ct)
        {
            // Gap del brief detectado al correr el test: ServicioBackup.EjecutarCorridaAsync
            // (Task 4) hace File.Move(rutaTmp, rutaFinal) al recibir Exitoso=true — si el fake no
            // deja un archivo real en `r`, ese Move tira FileNotFoundException, que
            // BackupProgramadoService.EjecutarCorridaSeguraAsync atrapa como "falla inesperada" y
            // solo loguea (por diseño), así que AgregarAsync nunca se llama. Se crea el archivo
            // vacío acá para que el fake cumpla el mismo contrato que EjecutorPgDumpProceso real.
            File.WriteAllText(r, string.Empty);
            return Task.FromResult(new ResultadoEjecucionPgDump(true, null));
        }
    }

    private sealed class UserDataPathProviderFake : IUserDataPathProvider
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_" + Guid.NewGuid());
        public string GetDataDirectory() => _dir;
        public string GetDatabasePath() => Path.Combine(_dir, "stockapp.db");
        public string GetBackupsDirectory() => Path.Combine(_dir, "backups");
        public string GetLicenciaPath() => Path.Combine(_dir, "licencia.lic");
    }

    private static (BackupProgramadoService servicio, List<object> instanciasQueAgregaron, CorridaBackup? ultimaExitosaSemilla)
        Crear(CorridaBackup? ultimaExitosaSemilla = null)
    {
        var instancias = new List<object>();
        var services = new ServiceCollection();
        services.AddScoped<ICorridaBackupRepository>(_ => new CorridaBackupRepositoryEspiaFake(instancias) { UltimaExitosa = ultimaExitosaSemilla });
        services.AddScoped<IEjecutorPgDump, EjecutorPgDumpFake>();
        services.AddScoped<ServicioBackup>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ServicioBackup>>(NullLogger<ServicioBackup>.Instance);

        var sp = services.BuildServiceProvider();
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x;Database=y" })
            .Build();

        var servicio = new BackupProgramadoService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuracion,
            new UserDataPathProviderFake(),
            NullLogger<BackupProgramadoService>.Instance);

        return (servicio, instancias, ultimaExitosaSemilla);
    }

    [Fact]
    public async Task EjecutarCorridaSeguraAsync_DosCorridas_UsaUnScopeDistintoEnCadaUna()
    {
        var (servicio, instancias, _) = Crear();
        var directorio = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_dir_" + Guid.NewGuid());

        await servicio.EjecutarCorridaSeguraAsync(directorio, CancellationToken.None);

        // Gap del brief detectado al correr el test: ServicioBackup.EjecutarCorridaAsync (Task 4)
        // nombra el .dump con precisión de segundo (yyyyMMdd_HHmmss). Dos corridas dentro del
        // mismo segundo generan el mismo NombreArchivo -> el segundo File.Move choca contra un
        // destino existente -> IOException -> BackupProgramadoService la atrapa como "falla
        // inesperada" (por diseño) y esa corrida se pierde sin dejar registro. Es una colisión de
        // Task 4 (ya aprobada/mergeada), ajena a lo que este test verifica (scope-per-corrida);
        // se cruza el borde de segundo acá para no confundir esa colisión con el comportamiento
        // bajo prueba. Ver concern en el reporte de Task 5.
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        await servicio.EjecutarCorridaSeguraAsync(directorio, CancellationToken.None);

        Assert.Equal(2, instancias.Count);
        Assert.NotSame(instancias[0], instancias[1]);
    }

    [Fact]
    public async Task DebeCorrerAhoraAsync_SinCorridaPrevia_DevuelveTrue()
    {
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: null);

        Assert.True(await servicio.DebeCorrerAhoraAsync());
    }

    [Fact]
    public async Task DebeCorrerAhoraAsync_UltimaCorridaHaceMasDeDoceHoras_DevuelveTrue()
    {
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: new CorridaBackup
        {
            FinalizadaEn = DateTime.UtcNow.AddHours(-13), Resultado = ResultadoBackup.Exitosa, NombreArchivo = "x.dump",
        });

        Assert.True(await servicio.DebeCorrerAhoraAsync());
    }

    [Fact]
    public async Task DebeCorrerAhoraAsync_UltimaCorridaHaceMenosDeDoceHoras_DevuelveFalse()
    {
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: new CorridaBackup
        {
            FinalizadaEn = DateTime.UtcNow.AddHours(-1), Resultado = ResultadoBackup.Exitosa, NombreArchivo = "x.dump",
        });

        Assert.False(await servicio.DebeCorrerAhoraAsync());
    }
}
