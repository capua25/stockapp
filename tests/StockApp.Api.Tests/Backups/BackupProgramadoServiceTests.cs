using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Api.Backups;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Alertas;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Tests.Compartido;
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
        Crear(
            CorridaBackup? ultimaExitosaSemilla = null,
            IGuardiaCorridaBackup? guardia = null,
            INotificadorAlertas? notificador = null,
            IEjecutorPgDump? ejecutor = null)
    {
        var instancias = new List<object>();
        var services = new ServiceCollection();
        services.AddScoped<ICorridaBackupRepository>(_ => new CorridaBackupRepositoryEspiaFake(instancias) { UltimaExitosa = ultimaExitosaSemilla });
        services.AddScoped<IEjecutorPgDump>(_ => ejecutor ?? new EjecutorPgDumpFake());
        services.AddScoped<INotificadorAlertas>(_ => notificador ?? new NotificadorAlertasNulo());
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
            guardia ?? new GuardiaCorridaBackup(),
            NullLogger<BackupProgramadoService>.Instance);

        return (servicio, instancias, ultimaExitosaSemilla);
    }

    /// <summary>Guardia fake que siempre reporta "ocupada" -- simula una corrida manual (POST
    /// /backups) ya en curso cuando el PeriodicTimer del job automático tiquea.</summary>
    private sealed class GuardiaSiempreOcupadaFake : IGuardiaCorridaBackup
    {
        public bool SalirFueLlamado { get; private set; }
        public bool TryEntrar() => false;
        public void Salir() => SalirFueLlamado = true;
    }

    /// <summary>Ronda de fix 1/5 (Important): misma forma que el NotificadorAlertasFake de
    /// DisparadorBackupManualTests -- los proyectos de test no comparten código.</summary>
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

    /// <summary>Ronda de fix 1/5: a diferencia de EjecutorPgDumpFake (que devuelve
    /// Exitoso=false para simular un fallo ESPERABLE de pg_dump, que ServicioBackup ya captura y
    /// persiste), este fake TIRA -- simula el "error realmente inesperado" que ServicioBackup no
    /// atrapa (ver ServicioBackup.EjecutarCorridaAsync: la llamada a _ejecutor.EjecutarAsync no
    /// tiene try/catch propio). La excepción sale de ServicioBackup sin persistir fila ni
    /// notificar por su cuenta, y llega cruda al catch de última resistencia de
    /// EjecutarCorridaSeguraAsync.</summary>
    private sealed class EjecutorPgDumpQueExplotaFake : IEjecutorPgDump
    {
        public Task<ResultadoEjecucionPgDump> EjecutarAsync(string c, string r, CancellationToken ct)
            => throw new InvalidOperationException("pg_dump explotó de forma inesperada (simulado).");
    }

    [Fact]
    public async Task EjecutarCorridaSeguraAsync_GuardiaOcupada_NoLlamaAlServicioNiASalir()
    {
        // Concurrencia (fix/integridad-referencial): si ya hay una corrida en curso (ej. un
        // backup manual disparado desde POST /backups), el tick del job automático se salta
        // en vez de arrancar un segundo pg_dump simultáneo.
        var guardiaOcupada = new GuardiaSiempreOcupadaFake();
        var (servicio, instancias, _) = Crear(guardia: guardiaOcupada);
        var directorio = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_dir_" + Guid.NewGuid());

        await servicio.EjecutarCorridaSeguraAsync(directorio, CancellationToken.None);

        Assert.Empty(instancias);
        // No se llama a Salir: TryEntrar nunca devolvió true, así que este llamador nunca tomó
        // el turno -- llamar a Salir acá liberaría un turno que no es suyo.
        Assert.False(guardiaOcupada.SalirFueLlamado);
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

    /// <summary>
    /// Espera a que la secuencia de arranque de un BackgroundService ya iniciado produzca su
    /// efecto observable, con límite. Se comprobó que StopAsync NO alcanza para esperarla (el
    /// ExecuteTask puede completarse por la cancelación antes de que el efecto sea visible) y un
    /// Task.Delay fijo a ciegas es candidato a flaky bajo CI cargada: esto poliestea y corta apenas
    /// se cumple la condición, así que en la práctica tarda un par de milisegundos.
    ///
    /// bugfix/backups-endpoint-tests-flaky: el límite se mide con EsperaMonotonica (Stopwatch),
    /// no con reloj de pared -- ver EsperaMonotonica para la razón.
    /// </summary>
    private static async Task EsperarHastaAsync(Func<bool> condicion, string queSeEsperaba)
    {
        var cumplida = await EsperaMonotonica.HastaAsync(condicion, TimeSpan.FromSeconds(10));

        Assert.True(cumplida, queSeEsperaba);
    }

    /// <summary>Arma el servicio con la secuencia de arranque condenada a fallar (el repo explota
    /// al consultar la última exitosa, simulando Postgres que todavía no levantó), con logger y
    /// notificador inyectables. Compartido por los dos tests de esa ruta.</summary>
    private static BackupProgramadoService CrearConArranqueQueFalla(
        Microsoft.Extensions.Logging.ILogger<BackupProgramadoService> logger,
        INotificadorAlertas notificador)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICorridaBackupRepository, CorridaBackupRepositoryQueFallaAlConsultarFake>();
        services.AddScoped<IEjecutorPgDump, EjecutorPgDumpFake>();
        services.AddScoped<INotificadorAlertas>(_ => notificador);
        services.AddScoped<ServicioBackup>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ServicioBackup>>(NullLogger<ServicioBackup>.Instance);
        var sp = services.BuildServiceProvider();
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x;Database=y" })
            .Build();

        return new BackupProgramadoService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuracion,
            new UserDataPathProviderFake(),
            new GuardiaCorridaBackup(),
            logger);
    }

    [Fact]
    public async Task ExecuteAsync_FalloEnSecuenciaDeArranque_NoTumbaLaTareaDelServicioYQuedaLogueado()
    {
        var loggerEspia = new LoggerEspiaFake();
        var servicio = CrearConArranqueQueFalla(loggerEspia, new NotificadorAlertasNulo());

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

    /// <summary>Ronda de fix 1/5 (Important, confianza 90): el camino del scheduler (corre
    /// desatendido, de madrugada) quedaba sin ningún test que ejerciera la notificación agregada
    /// en la Task 4 -- a diferencia de DisparadorBackupManual, este camino no persiste ninguna
    /// fila, así que sin esta notificación una corrida programada que revienta antes de llegar
    /// a ServicioBackup no deja ningún rastro hacia afuera.</summary>
    [Fact]
    public async Task EjecutarCorridaSeguraAsync_FalloInesperado_NotificaSinPersistirFila()
    {
        var notificador = new NotificadorAlertasFake();
        var (servicio, instancias, _) = Crear(notificador: notificador, ejecutor: new EjecutorPgDumpQueExplotaFake());
        var directorio = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_dir_" + Guid.NewGuid());

        await servicio.EjecutarCorridaSeguraAsync(directorio, CancellationToken.None);

        // Este camino no persiste fila (a diferencia de DisparadorBackupManual.PersistirFallaAsync).
        Assert.Empty(instancias);

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
    }

    /// <summary>
    /// Fix IMPORTANTE (I4 del review final): CUARTO camino de fallo sin notificación. El catch de
    /// la SECUENCIA DE ARRANQUE solo hacía LogError -- ni fila ni ping. Si Postgres tarda en
    /// levantar en cada reinicio, el catch-up se pierde en cada boot y no queda ningún rastro
    /// hacia afuera del servidor, que es justamente el modo de falla que la feature elimina.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FalloEnSecuenciaDeArranque_NotificaElFalloHaciaAfuera()
    {
        var notificador = new NotificadorAlertasFake();
        var servicio = CrearConArranqueQueFalla(NullLogger<BackupProgramadoService>.Instance, notificador);

        await servicio.StartAsync(CancellationToken.None);
        await EsperarHastaAsync(
            () => notificador.Notificadas.Count > 0,
            "El fallo de la secuencia de arranque no notificó nada hacia afuera.");
        await servicio.StopAsync(CancellationToken.None);

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
        Assert.Contains("secuencia de arranque", notificada.MotivoFallo!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Heartbeat de arranque (fix IMPORTANTE I3) ──────────────────────────────
    //
    // El PeriodicTimer se ancla al BOOT del proceso, no a la última corrida: un reinicio a las 11h
    // de la última corrida empujaba la siguiente a t=23h, y con la ventana recomendada de 14h
    // (período 12h + grace 2h) healthchecks marcaba "down" CON EL SISTEMA SANO. Una falsa alarma
    // entrena al usuario a ignorar el canal -- peor que no tener canal.

    [Fact]
    public async Task ExecuteAsync_UltimaCorridaDentroDeLaVentana_MandaHeartbeatDeArranqueSinCorrerBackup()
    {
        var notificador = new NotificadorAlertasFake();
        var (servicio, instancias, _) = Crear(
            ultimaExitosaSemilla: new CorridaBackup
            {
                FinalizadaEn = DateTime.UtcNow.AddHours(-1),
                Resultado = ResultadoBackup.Exitosa,
                NombreArchivo = "backup_previo.dump",
                TamanioBytes = 1024,
            },
            notificador: notificador);

        await servicio.StartAsync(CancellationToken.None);
        await EsperarHastaAsync(
            () => notificador.Notificadas.Count > 0,
            "El arranque con el sistema sano no mandó ningún heartbeat: healthchecks marcaría "
            + "'down' tras un reinicio, con el sistema perfectamente sano.");
        await servicio.StopAsync(CancellationToken.None);

        // No corrió ningún backup: la última exitosa tiene 1h, no toca todavía.
        Assert.Empty(instancias);

        // Pero SÍ avisó que seguimos vivos, con la corrida exitosa como carga (el notificador la
        // traduce a un ping de heartbeat, sin el sufijo /fail).
        var heartbeat = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Exitosa, heartbeat.Resultado);
    }

    [Fact]
    public async Task ExecuteAsync_UltimaCorridaFueraDeLaVentana_CorreElBackupYNoMandaHeartbeatExtra()
    {
        // El heartbeat es SOLO para el caso sano-sin-nada-que-hacer: si toca correr, el ping lo
        // manda la corrida real (vía ServicioBackup), no este camino.
        var notificador = new NotificadorAlertasFake();
        var (servicio, instancias, _) = Crear(
            ultimaExitosaSemilla: new CorridaBackup
            {
                FinalizadaEn = DateTime.UtcNow.AddHours(-13),
                Resultado = ResultadoBackup.Exitosa,
                NombreArchivo = "backup_viejo.dump",
                TamanioBytes = 1024,
            },
            notificador: notificador);

        await servicio.StartAsync(CancellationToken.None);
        await EsperarHastaAsync(
            () => instancias.Count > 0, "El catch-up de arranque no corrió el backup atrasado.");
        await servicio.StopAsync(CancellationToken.None);

        Assert.Single(instancias);
    }

    [Fact]
    public async Task EnviarHeartbeatDeArranqueAsync_SinCorridaPrevia_NoNotificaNada()
    {
        // Instalación nueva: no hay nada que reportar como "sano". Este camino ni siquiera se
        // alcanza en producción (DebeCorrerAhoraAsync devuelve true sin corrida previa), pero el
        // guard defensivo tiene que estar cubierto.
        var notificador = new NotificadorAlertasFake();
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: null, notificador: notificador);

        await servicio.EnviarHeartbeatDeArranqueAsync(CancellationToken.None);

        Assert.Empty(notificador.Notificadas);
    }
}
