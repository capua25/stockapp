using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Api.Backups;
using StockApp.Application.Alertas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Api.Tests.Backups;

/// <summary>
/// Menor 11 del review adversarial: no existía ningún test dedicado de DisparadorBackupManual --
/// el bug de la fuga de semáforo (Importante 3) lo habría atrapado en un segundo.
/// </summary>
public class DisparadorBackupManualTests
{
    /// <summary>Simula IUserDataPathProvider.GetBackupsDirectory() devolviendo string vacío o
    /// explotando -- deploy-vps.md documenta que devuelve "" si $HOME/.local/share no existe, un
    /// caso real ya visto en un deploy.</summary>
    private sealed class UserDataPathProviderQueExplotaFake : IUserDataPathProvider
    {
        public string GetDataDirectory() => throw new InvalidOperationException("boom");
        public string GetDatabasePath() => throw new InvalidOperationException("boom");
        public string GetBackupsDirectory() => throw new InvalidOperationException("boom");
        public string GetLicenciaPath() => throw new InvalidOperationException("boom");
        public string GetLogsDirectory() => throw new InvalidOperationException("boom");
    }

    private sealed class UserDataPathProviderFake : IUserDataPathProvider, IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "DisparadorBackupManualTests_" + Guid.NewGuid());
        public string GetDataDirectory() => _dir;
        public string GetDatabasePath() => Path.Combine(_dir, "stockapp.db");
        public string GetBackupsDirectory() => Path.Combine(_dir, "backups");
        public string GetLicenciaPath() => Path.Combine(_dir, "licencia.lic");
        public string GetLogsDirectory() => Path.Combine(_dir, "logs");

        /// <summary>Menor 8 del review adversarial (segunda pasada): el camino feliz de
        /// Disparar() llega a Directory.CreateDirectory(directorio) y crea un directorio real
        /// bajo Path.GetTempPath() -- sin este Dispose quedaba basura acumulándose en disco en
        /// cada corrida de la suite.</summary>
        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
    }

    private static IConfiguration CrearConfiguracion(bool conConnectionString = true)
    {
        var datos = conConnectionString
            ? new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x;Database=y" }
            : new Dictionary<string, string?>();
        return new ConfigurationBuilder().AddInMemoryCollection(datos).Build();
    }

    private static DisparadorBackupManual Crear(
        IGuardiaCorridaBackup guardia, IUserDataPathProvider paths, IConfiguration? configuracion = null)
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        return new DisparadorBackupManual(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuracion ?? CrearConfiguracion(),
            paths,
            guardia,
            NullLogger<DisparadorBackupManual>.Instance);
    }

    [Fact]
    public void Disparar_PathProviderExplota_LaGuardiaQuedaLibreParaElProximoIntento()
    {
        // Bug real (fix/integridad-referencial, IMPORTANTE 3): TryEntrar() toma el turno ANTES de
        // resolver connectionString/directorio -- si _paths.GetBackupsDirectory() explota (deploy-
        // vps.md: puede devolver string vacío o, en este test, tirar directamente si
        // $HOME/.local/share no existe), la excepción sale de Disparar() SIN pasar nunca por el
        // finally { _guardia.Salir(); } que vive en EjecutarEnBackgroundAsync -- esa Task nunca
        // llegó a lanzarse. El semáforo Singleton queda tomado para siempre: ni backups manuales
        // (409 eterno) ni el job automático (BackupProgramadoService se salta cada tick) vuelven a
        // correr hasta reiniciar el proceso.
        var guardia = new GuardiaCorridaBackup();
        var disparador = Crear(guardia, new UserDataPathProviderQueExplotaFake());

        Assert.Throws<InvalidOperationException>(() => disparador.Disparar(usuarioId: 1));

        Assert.True(guardia.TryEntrar(), "La guardia quedó tomada para siempre tras el fallo.");
    }

    [Fact]
    public void Disparar_ConnectionStringFaltante_LaGuardiaQuedaLibreParaElProximoIntento()
    {
        // Mismo bug, otro disparador posible de la misma línea sin try/finally: falta
        // ConnectionStrings:Default en configuración.
        var guardia = new GuardiaCorridaBackup();
        using var paths = new UserDataPathProviderFake();
        var disparador = Crear(guardia, paths, CrearConfiguracion(conConnectionString: false));

        Assert.Throws<InvalidOperationException>(() => disparador.Disparar(usuarioId: 1));

        Assert.True(guardia.TryEntrar(), "La guardia quedó tomada para siempre tras el fallo.");
    }

    /// <summary>
    /// MENOR 7 del review adversarial (segunda pasada, aclaración pedida): este test guarda el
    /// guard de REENTRANCIA (ya hay una corrida en curso -> corta ANTES de tocar paths), NO el fix
    /// de la fuga de semáforo (IMPORTANTE 3, cubierto arriba por
    /// Disparar_PathProviderExplota_.../Disparar_ConnectionStringFaltante_...). Revertir ESE fix
    /// (sacar el try/catch que libera la guardia si paths/configuration explotan) NO rompe este
    /// test: acá la guardia nunca se libera dentro de Disparar() -- la toma el propio test antes
    /// de llamar, y TryEntrar() de Disparar() corta con `return false` antes de llegar a esa
    /// sección.
    /// </summary>
    [Fact]
    public void Disparar_GuardiaYaOcupada_DevuelveFalseYNoConsultaPaths()
    {
        var guardia = new GuardiaCorridaBackup();
        Assert.True(guardia.TryEntrar()); // simula una corrida ya en curso
        var disparador = Crear(guardia, new UserDataPathProviderQueExplotaFake());

        // Si llegara a consultar paths (que explota), este Assert nunca se alcanzaría -- prueba
        // indirectamente que el guard de "ya ocupada" corta ANTES de tocar paths/configuration.
        var resultado = disparador.Disparar(usuarioId: 1);

        Assert.False(resultado);
    }

    private sealed class CorridaBackupRepositoryEspiaFake : ICorridaBackupRepository
    {
        public List<CorridaBackup> Agregadas { get; } = new();

        public Task<int> AgregarAsync(CorridaBackup corrida)
        {
            Agregadas.Add(corrida);
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());
        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());
        public Task<CorridaBackup?> ObtenerPorIdAsync(int id) => Task.FromResult<CorridaBackup?>(null);
        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync() => Task.FromResult<CorridaBackup?>(null);
        public Task EliminarAsync(int id) => Task.CompletedTask;
    }

    /// <summary>Importante 6 del review adversarial: antes, un fallo inesperado dentro de
    /// EjecutarEnBackgroundAsync (ej. la propia resolución de ServicioBackup vía DI, o cualquier
    /// error que ServicioBackup.EjecutarCorridaAsync no llegara a atrapar él mismo) sólo se
    /// logueaba -- la UI le dijo al usuario "Backup iniciado, actualizá en unos minutos" y esa
    /// fila NUNCA aparecía en GET /backups. Ahora el catch persiste una CorridaBackup Fallida con
    /// el motivo, para que el humano que apretó el botón vea qué pasó.</summary>
    [Fact]
    public async Task Disparar_FalloInesperadoEnBackground_PersisteUnaCorridaBackupFallida()
    {
        var guardia = new GuardiaCorridaBackup();
        var espia = new CorridaBackupRepositoryEspiaFake();
        var services = new ServiceCollection();
        // A propósito NO se registra ServicioBackup/IEjecutorPgDump: simula el "error realmente
        // inesperado" documentado en el catch de EjecutarEnBackgroundAsync (acá, la propia
        // resolución del servicio vía DI explota) -- no un fallo esperable de pg_dump, que
        // ServicioBackup ya persiste por su cuenta.
        services.AddScoped<ICorridaBackupRepository>(_ => espia);
        var sp = services.BuildServiceProvider();
        using var paths = new UserDataPathProviderFake();

        var disparador = new DisparadorBackupManual(
            sp.GetRequiredService<IServiceScopeFactory>(),
            CrearConfiguracion(),
            paths,
            guardia,
            NullLogger<DisparadorBackupManual>.Instance);

        Assert.True(disparador.Disparar(usuarioId: 7));

        // Menor 8 del review adversarial (segunda pasada): antes, un Task.Delay(300ms) a ciegas
        // esperaba que EjecutarEnBackgroundAsync (fire-and-forget, privado) terminara -- candidato
        // a flaky bajo CI cargada. DisparadorBackupManual.UltimaCorridaEnBackgroundParaTests
        // (seam de test) expone esa MISMA Task -- awaitearla es determinístico: espera exactamente
        // lo que hace falta, ni un tick más ni menos.
        await disparador.UltimaCorridaEnBackgroundParaTests!;

        var fallida = Assert.Single(espia.Agregadas);
        Assert.Equal(ResultadoBackup.Fallida, fallida.Resultado);
        Assert.Equal(7, fallida.UsuarioId);
        Assert.Null(fallida.NombreArchivo);
        Assert.False(string.IsNullOrWhiteSpace(fallida.MotivoFallo));

        // Y la guardia también quedó libre (finally de EjecutarEnBackgroundAsync, sin relación con
        // Importante 6 pero verificado igual acá de paso).
        Assert.True(guardia.TryEntrar());
    }

    /// <summary>Misma forma que el NotificadorAlertasFake de ServicioBackupTests (Task 3) --
    /// los proyectos de test no comparten código, así que cada uno declara el suyo.</summary>
    private sealed class NotificadorAlertasFake : INotificadorAlertas
    {
        public List<CorridaBackup> Notificadas { get; } = new();

        public Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
        {
            Notificadas.Add(corrida);
            return Task.CompletedTask;
        }

        public Task<ResultadoPruebaAlertaDto> ProbarPingAsync(string url, CancellationToken ct = default)
            => Task.FromResult(new ResultadoPruebaAlertaDto(true, 200, "ok"));
    }

    /// <summary>Mismo patrón que Disparar_FalloInesperadoEnBackground_PersisteUnaCorridaBackupFallida
    /// de arriba (a propósito NO se registra ServicioBackup/IEjecutorPgDump, simulando el "error
    /// realmente inesperado" que dispara el catch de última resistencia), extraído a helper para
    /// el test de Task 4 que además registra el notificador.</summary>
    private static (DisparadorBackupManual Disparador, CorridaBackupRepositoryEspiaFake Repo) CrearDisparadorQueFallaInesperadamente(
        INotificadorAlertas notificador)
    {
        var guardia = new GuardiaCorridaBackup();
        var espia = new CorridaBackupRepositoryEspiaFake();
        var services = new ServiceCollection();
        services.AddScoped<ICorridaBackupRepository>(_ => espia);
        services.AddScoped<INotificadorAlertas>(_ => notificador);
        var sp = services.BuildServiceProvider();
        var paths = new UserDataPathProviderFake();

        var disparador = new DisparadorBackupManual(
            sp.GetRequiredService<IServiceScopeFactory>(),
            CrearConfiguracion(),
            paths,
            guardia,
            NullLogger<DisparadorBackupManual>.Instance);

        return (disparador, espia);
    }

    /// <summary>Task 4 (canal de alerta de backups): el camino de "fallo inesperado" no pasa por
    /// ServicioBackup -- si no se engancha acá, este modo de falla queda mudo.</summary>
    [Fact]
    public async Task Disparar_FalloInesperado_PersisteLaFallaYLaNotifica()
    {
        var notificador = new NotificadorAlertasFake();
        var (disparador, repo) = CrearDisparadorQueFallaInesperadamente(notificador);

        disparador.Disparar(usuarioId: 1);
        await disparador.UltimaCorridaEnBackgroundParaTests!;

        var corrida = Assert.Single(repo.Agregadas);
        Assert.Equal(ResultadoBackup.Fallida, corrida.Resultado);
        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
    }

    /// <summary>Repo que no puede persistir: simula la propia base de la app caída, que es
    /// justamente uno de los motivos típicos por los que se llega a PersistirFallaAsync.</summary>
    private sealed class CorridaBackupRepositoryQueNoPersisteFake : ICorridaBackupRepository
    {
        public Task<int> AgregarAsync(CorridaBackup corrida)
            => throw new InvalidOperationException("la base no responde (simulado)");

        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());
        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());
        public Task<CorridaBackup?> ObtenerPorIdAsync(int id) => Task.FromResult<CorridaBackup?>(null);
        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync() => Task.FromResult<CorridaBackup?>(null);
        public Task EliminarAsync(int id) => Task.CompletedTask;
    }

    /// <summary>
    /// Fix (MENOR M1 del review final): la notificación vivía DENTRO del try de persistencia, así
    /// que si AgregarAsync tiraba ni siquiera se INTENTABA avisar hacia afuera -- y el escenario en
    /// el que la persistencia falla (la base de la app caída) es exactamente aquel en el que el
    /// aviso externo es el ÚNICO rastro que puede sobrevivir. Los dos rastros son independientes.
    /// </summary>
    [Fact]
    public async Task Disparar_FalloInesperadoYLaPersistenciaTambienFalla_IgualNotificaHaciaAfuera()
    {
        var notificador = new NotificadorAlertasFake();
        var guardia = new GuardiaCorridaBackup();
        var services = new ServiceCollection();
        // A propósito NO se registra ServicioBackup: fuerza el catch de última resistencia.
        services.AddScoped<ICorridaBackupRepository, CorridaBackupRepositoryQueNoPersisteFake>();
        services.AddScoped<INotificadorAlertas>(_ => notificador);
        var sp = services.BuildServiceProvider();
        using var paths = new UserDataPathProviderFake();

        var disparador = new DisparadorBackupManual(
            sp.GetRequiredService<IServiceScopeFactory>(),
            CrearConfiguracion(),
            paths,
            guardia,
            NullLogger<DisparadorBackupManual>.Instance);

        Assert.True(disparador.Disparar(usuarioId: 3));
        await disparador.UltimaCorridaEnBackgroundParaTests!;

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
        Assert.Equal(3, notificada.UsuarioId);

        // Y el fallo de persistencia tampoco tumbó nada: la guardia quedó libre.
        Assert.True(guardia.TryEntrar());
    }
}
