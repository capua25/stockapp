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

    /// <summary>Simula el caso real que motiva el Fix 1: la API arranca antes de que Postgres
    /// esté listo (o Postgres se reinicia) y la consulta de ObtenerUltimaExitosaAsync -llamada
    /// desde DebeCorrerAhoraAsync, dentro de la secuencia de ARRANQUE de ExecuteAsync- explota.</summary>
    private sealed class CorridaBackupRepositoryQueFallaAlConsultarFake : ICorridaBackupRepository
    {
        public Task<int> AgregarAsync(CorridaBackup corrida) => Task.FromResult(1);
        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());
        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());
        public Task<CorridaBackup?> ObtenerPorIdAsync(int id) => Task.FromResult<CorridaBackup?>(null);
        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync()
            => throw new InvalidOperationException("Postgres no está listo (simulado).");
        public Task EliminarAsync(int id) => Task.CompletedTask;
    }

    /// <summary>Logger espía: no hay forma de assertar "no lanza" sin distinguir de qué rama vino
    /// (ver Fix 2 del report de la Task 5) — acá lo que importa es que el fallo de arranque quedó
    /// REGISTRADO, no sólo que no tumbó el proceso.</summary>
    private sealed class LoggerEspiaFake : Microsoft.Extensions.Logging.ILogger<BackupProgramadoService>
    {
        public List<Exception?> ErroresLogueados { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error)
                ErroresLogueados.Add(exception);
        }
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

        // Ya NO hace falta cruzar el borde de segundo acá: el Fix 2 de la Task 5 (ver report)
        // agregó milisegundos al nombre del .dump (yyyyMMdd_HHmmssfff), así que dos corridas
        // consecutivas dentro del mismo segundo ya no colisionan de nombre.
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

    [Fact]
    public async Task ExecuteAsync_FalloEnSecuenciaDeArranque_NoTumbaLaTareaDelServicioYQuedaLogueado()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICorridaBackupRepository, CorridaBackupRepositoryQueFallaAlConsultarFake>();
        services.AddScoped<IEjecutorPgDump, EjecutorPgDumpFake>();
        services.AddScoped<ServicioBackup>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ServicioBackup>>(NullLogger<ServicioBackup>.Instance);
        var sp = services.BuildServiceProvider();
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x;Database=y" })
            .Build();
        var loggerEspia = new LoggerEspiaFake();

        var servicio = new BackupProgramadoService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuracion,
            new UserDataPathProviderFake(),
            loggerEspia);

        // Ciclo de vida REAL de un BackgroundService (el mismo que usa el host de ASP.NET Core en
        // producción), no una llamada directa a un método interno: así se ejerce exactamente la
        // ruta que rompía antes del fix.
        await servicio.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        // Comportamiento real, no ausencia de excepción: sin el fix, ExecuteTask queda Faulted
        // con la InvalidOperationException del repo (fuga fuera de ExecuteAsync) y esta línea
        // falla. Con el fix, la excepción fue atrapada y el servicio sigue vivo esperando el
        // próximo tick del PeriodicTimer.
        Assert.NotNull(servicio.ExecuteTask);
        Assert.False(servicio.ExecuteTask!.IsFaulted, servicio.ExecuteTask.Exception?.ToString());
        Assert.False(servicio.ExecuteTask.IsCompleted);

        // Y el fallo quedó registrado -> no es un fallo silencioso.
        Assert.Contains(loggerEspia.ErroresLogueados, ex => ex is InvalidOperationException);

        await servicio.StopAsync(CancellationToken.None);
    }
}
