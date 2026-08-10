using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Application.Alertas;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Backups;

public class ServicioBackupTests
{
    private static readonly DateTime Ahora = new(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc);

    private sealed class EjecutorPgDumpFake : IEjecutorPgDump
    {
        private readonly bool _exitoso;
        private readonly string? _mensajeError;
        public bool Invocado { get; private set; }

        public EjecutorPgDumpFake(bool exitoso, string? mensajeError = null)
        {
            _exitoso = exitoso;
            _mensajeError = mensajeError;
        }

        public Task<ResultadoEjecucionPgDump> EjecutarAsync(
            string connectionString, string rutaDestino, CancellationToken cancellationToken)
        {
            Invocado = true;
            if (_exitoso)
                File.WriteAllBytes(rutaDestino, new byte[] { 1, 2, 3, 4 });
            return Task.FromResult(new ResultadoEjecucionPgDump(_exitoso, _mensajeError));
        }
    }

    private sealed class CorridaBackupRepositoryFake : ICorridaBackupRepository
    {
        public List<CorridaBackup> Corridas { get; } = new();
        private int _siguienteId = 1;

        public Task<int> AgregarAsync(CorridaBackup corrida)
        {
            corrida.Id = _siguienteId++;
            Corridas.Add(corrida);
            return Task.FromResult(corrida.Id);
        }

        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)Corridas.OrderByDescending(c => c.FinalizadaEn).ToList());

        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)Corridas
                .Where(c => c.Resultado == ResultadoBackup.Exitosa)
                .OrderByDescending(c => c.FinalizadaEn).ToList());

        public Task<CorridaBackup?> ObtenerPorIdAsync(int id)
            => Task.FromResult(Corridas.FirstOrDefault(c => c.Id == id));

        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync()
            => Task.FromResult(Corridas
                .Where(c => c.Resultado == ResultadoBackup.Exitosa)
                .OrderByDescending(c => c.FinalizadaEn).FirstOrDefault());

        public Task EliminarAsync(int id)
        {
            Corridas.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }
    }

    private static string CrearDirectorioTemporal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ServicioBackupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Exitosa_PersisteCorridaExitosaConArchivoYTamanio()
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: true);
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Exitosa, corrida.Resultado);
        Assert.NotNull(corrida.NombreArchivo);
        Assert.True(corrida.TamanioBytes > 0);
        Assert.Null(corrida.MotivoFallo);
        Assert.True(File.Exists(Path.Combine(directorio, corrida.NombreArchivo!)));
    }

    [Fact]
    public async Task EjecutarCorridaAsync_SinUsuarioId_PersisteCorridaConUsuarioIdNull()
    {
        // Job automático (BackupProgramadoService) o llamador que no pasa el parámetro nuevo:
        // el default debe seguir siendo null, sin cambiar el comportamiento existente.
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: true);
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Null(corrida.UsuarioId);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_ConUsuarioId_PersisteCorridaConEseUsuarioId()
    {
        // Disparo manual (POST /backups, DisparadorBackupManual): el actor viaja hasta la
        // fila persistida, exitosa o fallida.
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: true);
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None, usuarioId: 7);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(7, corrida.UsuarioId);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_FallidaConUsuarioId_PersisteCorridaFallidaConEseUsuarioId()
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: false, mensajeError: "pg_dump: fallo simulado");
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None, usuarioId: 7);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Fallida, corrida.Resultado);
        Assert.Equal(7, corrida.UsuarioId);
    }

    [Theory]
    [InlineData("pg_dump: no se encontró el ejecutable")]
    [InlineData("pg_dump: password authentication failed for user \"stockapp\"")]
    [InlineData("pg_dump excedió el timeout de 300 segundos.")]
    [InlineData("pg_dump: error: could not write to output file: No space left on device")]
    public async Task EjecutarCorridaAsync_Fallida_PersisteCorridaFallidaConMotivoYSinArchivo(string motivo)
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: false, mensajeError: motivo);
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Fallida, corrida.Resultado);
        Assert.Null(corrida.NombreArchivo);
        Assert.Null(corrida.TamanioBytes);
        Assert.Equal(motivo, corrida.MotivoFallo);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Fallida_NoDejaArchivoTmpHuerfano()
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFakeQueEscribeYFalla();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(directorio, "*.tmp"));
    }

    private sealed class EjecutorPgDumpFakeQueEscribeYFalla : IEjecutorPgDump
    {
        public Task<ResultadoEjecucionPgDump> EjecutarAsync(
            string connectionString, string rutaDestino, CancellationToken cancellationToken)
        {
            // Simula un pg_dump que alcanzó a escribir bytes parciales antes de fallar (ej.
            // disco lleno a mitad de la escritura) — exactamente el caso que .tmp existe para.
            File.WriteAllBytes(rutaDestino, new byte[] { 9, 9 });
            return Task.FromResult(new ResultadoEjecucionPgDump(false, "disco lleno a mitad de escritura"));
        }
    }

    /// <summary>Simula el caso real del Fix 2 (report Task 5): pg_dump termina bien y escribe el
    /// .tmp, pero el rename atómico a destino falla (colisión, disco lleno, permisos, antivirus
    /// reteniendo el handle en Windows). Bloquea el directorio DESPUÉS de escribir el .tmp -no
    /// antes- porque si no, ni el propio ejecutor podría escribir el archivo.</summary>
    private sealed class EjecutorPgDumpFakeQueBloqueaDirectorioTrasEscribir : IEjecutorPgDump
    {
        public Task<ResultadoEjecucionPgDump> EjecutarAsync(
            string connectionString, string rutaDestino, CancellationToken cancellationToken)
        {
            File.WriteAllBytes(rutaDestino, new byte[] { 1, 2, 3 });
            var directorio = Path.GetDirectoryName(rutaDestino)!;
            File.SetUnixFileMode(directorio, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return Task.FromResult(new ResultadoEjecucionPgDump(true, null));
        }
    }

    [Fact]
    public async Task EjecutarCorridaAsync_FileMoveFalla_PersisteCorridaFallidaConMotivoYNoLanza()
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFakeQueBloqueaDirectorioTrasEscribir();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        try
        {
            // Filesystem real, sin fakes de File.Move, siguiendo el patrón de
            // LimpiarTmpHuerfanos_ArchivoSinPermisoDeBorrado_NoLanzaYLoDejaEnDisco.
            var ex = await Record.ExceptionAsync(
                () => svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None));

            Assert.Null(ex);

            // Comportamiento real, no ausencia de excepción: si el fallo del File.Move quedara
            // sin registrar, repo.Corridas estaría vacío acá (el bug original: la excepción
            // escapaba ANTES de _corridas.AgregarAsync).
            var corrida = Assert.Single(repo.Corridas);
            Assert.Equal(ResultadoBackup.Fallida, corrida.Resultado);
            Assert.Null(corrida.NombreArchivo);
            Assert.Null(corrida.TamanioBytes);
            Assert.False(string.IsNullOrWhiteSpace(corrida.MotivoFallo));
        }
        finally
        {
            File.SetUnixFileMode(directorio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void LimpiarTmpHuerfanos_BorraSoloArchivosTmp()
    {
        var directorio = CrearDirectorioTemporal();
        File.WriteAllBytes(Path.Combine(directorio, "huerfano1.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directorio, "huerfano2.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directorio, "backup_valido.dump"), new byte[] { 1 });
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        svc.LimpiarTmpHuerfanos(directorio);

        var restantes = Directory.GetFiles(directorio).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain("huerfano1.tmp", restantes);
        Assert.DoesNotContain("huerfano2.tmp", restantes);
        Assert.Contains("backup_valido.dump", restantes);
    }

    [Fact]
    public void LimpiarTmpHuerfanos_DirectorioInexistente_NoLanzaYNoCreaElDirectorio()
    {
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);
        var directorioInexistente = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid());

        svc.LimpiarTmpHuerfanos(directorioInexistente);

        // Assert explícito (pre-flight scan, corregido): el guard "if (!Directory.Exists(...))
        // return;" no debe tener el efecto secundario de crear el directorio al comprobarlo.
        Assert.False(Directory.Exists(directorioInexistente));
    }

    [Fact]
    public async Task EjecutarCorridaAsync_TrasCorridaExitosa_AplicaRetencionYBorraLoQueSobra()
    {
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();

        // Sembrar 90 corridas exitosas viejas (cada 12h, desde hace 45 días) con su archivo real
        // en disco, para que la política tenga algo real que borrar tras la corrida de HOY.
        for (var i = 1; i <= 90; i++)
        {
            var finalizadaEn = Ahora.AddHours(-12 * i);
            var nombre = $"vieja_{i}.dump";
            File.WriteAllBytes(Path.Combine(directorio, nombre), new byte[] { 1 });
            await repo.AgregarAsync(new CorridaBackup
            {
                IniciadaEn = finalizadaEn.AddMinutes(-1), FinalizadaEn = finalizadaEn,
                Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombre, TamanioBytes = 1,
            });
        }
        var cantidadAntes = repo.Corridas.Count;

        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);
        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        // +1 por la corrida de hoy, pero MENOS que antes porque la retención barrió las viejas.
        Assert.True(repo.Corridas.Count < cantidadAntes + 1);
        // Las filas borradas de la DB tampoco dejan archivo huérfano en disco.
        foreach (var nombreBorrado in Enumerable.Range(1, 90).Select(i => $"vieja_{i}.dump")
                     .Except(repo.Corridas.Select(c => c.NombreArchivo).OfType<string>()))
        {
            Assert.False(File.Exists(Path.Combine(directorio, nombreBorrado)));
        }
    }

    [Fact]
    public async Task EjecutarCorridaAsync_TrasCorridaFallida_NoAplicaRetencion()
    {
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();

        // Mismo dataset que EjecutarCorridaAsync_TrasCorridaExitosa_AplicaRetencionYBorraLoQueSobra:
        // 90 corridas exitosas viejas, muy por encima de los niveles de retención (6 recientes +
        // 7 diarias + 4 semanales), así que hay candidatas REALES a borrado si la política llegara
        // a ejecutarse. Con un repo vacío el test no distingue "no se llamó a la retención" de
        // "se llamó y no había nada que borrar" — con este dataset sí lo distingue.
        for (var i = 1; i <= 90; i++)
        {
            var finalizadaEn = Ahora.AddHours(-12 * i);
            var nombre = $"vieja_{i}.dump";
            File.WriteAllBytes(Path.Combine(directorio, nombre), new byte[] { 1 });
            await repo.AgregarAsync(new CorridaBackup
            {
                IniciadaEn = finalizadaEn.AddMinutes(-1), FinalizadaEn = finalizadaEn,
                Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombre, TamanioBytes = 1,
            });
        }
        var idsAntes = repo.Corridas.Select(c => c.Id).ToList();

        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: false, mensajeError: "falló"), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        // No debe lanzar ni intentar listar/borrar nada más allá de agregar la corrida fallida.
        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        // +1 por la corrida fallida de hoy; ninguna de las 90 viejas fue tocada por la retención.
        Assert.Equal(91, repo.Corridas.Count);
        Assert.All(idsAntes, id => Assert.Contains(repo.Corridas, c => c.Id == id));
        foreach (var i in Enumerable.Range(1, 90))
            Assert.True(File.Exists(Path.Combine(directorio, $"vieja_{i}.dump")));
    }

    [Fact]
    public void LimpiarTmpHuerfanos_ArchivoSinPermisoDeBorrado_NoLanzaYLoDejaEnDisco()
    {
        var directorio = CrearDirectorioTemporal();
        File.WriteAllBytes(Path.Combine(directorio, "bloqueado.tmp"), new byte[] { 1 });
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        // Quitamos el permiso de escritura del directorio (dejamos lectura+ejecución): en Linux
        // esto hace que File.Delete falle con UnauthorizedAccessException (no IOException) —
        // filesystem real, sin fakes, siguiendo el patrón de los demás tests de este archivo. Es
        // el análogo de un .tmp/.dump bloqueado por permisos/antivirus en un servidor Windows.
        File.SetUnixFileMode(directorio, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var ex = Record.Exception(() => svc.LimpiarTmpHuerfanos(directorio));

            Assert.Null(ex);
            Assert.True(File.Exists(Path.Combine(directorio, "bloqueado.tmp")));
        }
        finally
        {
            File.SetUnixFileMode(directorio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Fix 2 del review final E1: antes, la fila de una corrida seleccionada por la
    /// retención se borraba de la DB SIN importar si el archivo en disco se pudo borrar. Este
    /// test pone el archivo bajo un directorio sin permiso de escritura (mismo truco que
    /// LimpiarTmpHuerfanos_ArchivoSinPermisoDeBorrado, pero en un SUBdirectorio -no en
    /// `directorio` mismo- para que la corrida de HOY pueda seguir escribiendo su propio dump
    /// normalmente y la retención llegue a ejecutarse). Sin el fix, este test falla porque la
    /// fila desaparece igual aunque el archivo siga en disco.</summary>
    [Fact]
    public async Task EjecutarCorridaAsync_RetencionConArchivoQueNoSePudoBorrar_NoEliminaLaFilaDeLaCorrida()
    {
        var directorio = CrearDirectorioTemporal();
        var subdirBloqueado = Path.Combine(directorio, "bloqueado");
        Directory.CreateDirectory(subdirBloqueado);
        var nombreArchivoBloqueado = Path.Combine("bloqueado", "vieja_bloqueada.dump");
        File.WriteAllBytes(Path.Combine(directorio, nombreArchivoBloqueado), new byte[] { 1 });

        var repo = new CorridaBackupRepositoryFake();
        var corridaBloqueada = new CorridaBackup
        {
            IniciadaEn = Ahora.AddDays(-40).AddMinutes(-1), FinalizadaEn = Ahora.AddDays(-40),
            Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombreArchivoBloqueado, TamanioBytes = 1,
        };
        await repo.AgregarAsync(corridaBloqueada);

        // Candidatas viejas "normales" adicionales (0.5 a 10 días atrás): con la corridaBloqueada
        // a 40 días, queda muy por fuera de los 6 recientes / 7 días / 4 semanas sin importar
        // estas — es una candidata REAL a borrado, no una que la política salvaría de todas formas.
        for (var i = 1; i <= 20; i++)
        {
            var finalizadaEn = Ahora.AddHours(-12 * i);
            var nombre = $"vieja_{i}.dump";
            File.WriteAllBytes(Path.Combine(directorio, nombre), new byte[] { 1 });
            await repo.AgregarAsync(new CorridaBackup
            {
                IniciadaEn = finalizadaEn.AddMinutes(-1), FinalizadaEn = finalizadaEn,
                Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombre, TamanioBytes = 1,
            });
        }

        File.SetUnixFileMode(subdirBloqueado, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

            await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

            Assert.Contains(repo.Corridas, c => c.Id == corridaBloqueada.Id);
            Assert.True(File.Exists(Path.Combine(directorio, nombreArchivoBloqueado)));
        }
        finally
        {
            File.SetUnixFileMode(subdirBloqueado, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ── ReconciliarDumpHuerfanosAsync (Fix 1 del re-review final E1) ──────────

    /// <summary>Escenario completo de restore que motiva este fix (report Fix 1): el admin toma un
    /// backup de seguridad y restaura la base a un punto de hace 10 días. CorridasBackup vuelve a
    /// ese estado -- el propio backup de seguridad recién tomado queda sin fila, exactamente como
    /// cualquier otro .dump generado después del punto de restore. La API se reinicia y este
    /// barrido corre. ANTES este método (entonces "LimpiarDumpHuerfanosAsync") BORRABA el archivo;
    /// ahora lo reconcilia: da de alta su fila y lo deja en disco, descargable desde Mantenimiento,
    /// para que PoliticaRetencion decida su destino en la corrida siguiente.</summary>
    [Fact]
    public async Task ReconciliarDumpHuerfanosAsync_EscenarioDeRestore_DaDeAltaLaCorridaSinBorrarElArchivo()
    {
        var directorio = CrearDirectorioTemporal();
        var iniciadaEn = new DateTime(2026, 7, 17, 3, 0, 0, 0, DateTimeKind.Utc); // 10 días antes de Ahora
        var nombreArchivo = $"backup_{iniciadaEn:yyyyMMdd_HHmmssfff}.dump";
        var ruta = Path.Combine(directorio, nombreArchivo);
        File.WriteAllBytes(ruta, new byte[] { 1, 2, 3, 4, 5 });
        File.SetLastWriteTimeUtc(ruta, Ahora.AddDays(-10)); // bien fuera del margen de gracia
        var ultimaEscrituraReal = File.GetLastWriteTimeUtc(ruta); // lo que el filesystem realmente guardó
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.ReconciliarDumpHuerfanosAsync(directorio, Ahora, TimeSpan.FromMinutes(15));

        Assert.True(File.Exists(ruta)); // nunca se borra
        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(nombreArchivo, corrida.NombreArchivo);
        Assert.Equal(ResultadoBackup.Exitosa, corrida.Resultado);
        Assert.Equal(5, corrida.TamanioBytes);
        Assert.Equal(iniciadaEn, corrida.IniciadaEn);
        Assert.Equal(ultimaEscrituraReal, corrida.FinalizadaEn);
        // Marca de fila reconstruida (sin agregar columnas nuevas a la tabla) -- MotivoFallo es
        // siempre null en una corrida Exitosa real. Fix (MINOR, tercer review final E1): antes
        // solo chequeaba !IsNullOrWhiteSpace, que pasa igual si alguien cambia la constante por
        // "x" -- ahora compara contra el valor real de la constante.
        Assert.Equal(ServicioBackup.MarcaFilaReconciliada, corrida.MotivoFallo);
    }

    /// <summary>Criterio del usuario: nunca borrar un archivo que no se reconoce. Un nombre que no
    /// matchea "backup_yyyyMMdd_HHmmssfff.dump" (ej. alguien copió un .dump ahí a mano) no se
    /// puede reconciliar -- no hay de dónde reconstruir IniciadaEn -- y tampoco se borra.</summary>
    [Fact]
    public async Task ReconciliarDumpHuerfanosAsync_NombreNoParseable_NoLoBorraNiLoReconcilia()
    {
        var directorio = CrearDirectorioTemporal();
        var ruta = Path.Combine(directorio, "respaldo_manual_admin.dump");
        File.WriteAllBytes(ruta, new byte[] { 1 });
        File.SetLastWriteTimeUtc(ruta, Ahora.AddDays(-10)); // bien fuera del margen de gracia
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.ReconciliarDumpHuerfanosAsync(directorio, Ahora, TimeSpan.FromMinutes(15));

        Assert.True(File.Exists(ruta));
        Assert.Empty(repo.Corridas);
    }

    [Fact]
    public async Task ReconciliarDumpHuerfanosAsync_SinFilaPeroDentroDelMargenDeGracia_NoLoTocaAunSinFila()
    {
        var directorio = CrearDirectorioTemporal();
        var ruta = Path.Combine(directorio, "reciente.dump");
        File.WriteAllBytes(ruta, new byte[] { 1 });
        File.SetLastWriteTimeUtc(ruta, Ahora.AddMinutes(-1));
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        // El archivo no tiene fila en CorridasBackup todavía, pero es "reciente" (dentro del
        // margen de gracia) -- podría ser el rename atómico de una corrida en vuelo, a un paso
        // de que _corridas.AgregarAsync le dé su fila. No debe borrarse NI reconciliarse (una
        // reconciliación acá crearía una fila DUPLICADA cuando la real llegue).
        await svc.ReconciliarDumpHuerfanosAsync(directorio, Ahora, TimeSpan.FromMinutes(15));

        Assert.True(File.Exists(ruta));
        Assert.Empty(repo.Corridas);
    }

    [Fact]
    public async Task ReconciliarDumpHuerfanosAsync_ConFilaCorrespondienteAunqueViejo_LoDejaEnDiscoSinDuplicarLaFila()
    {
        var directorio = CrearDirectorioTemporal();
        var nombreArchivo = "backup_valido.dump";
        var ruta = Path.Combine(directorio, nombreArchivo);
        File.WriteAllBytes(ruta, new byte[] { 1 });
        File.SetLastWriteTimeUtc(ruta, Ahora.AddDays(-30));
        var repo = new CorridaBackupRepositoryFake();
        await repo.AgregarAsync(new CorridaBackup
        {
            IniciadaEn = Ahora.AddDays(-30), FinalizadaEn = Ahora.AddDays(-30),
            Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombreArchivo, TamanioBytes = 1,
        });
        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        await svc.ReconciliarDumpHuerfanosAsync(directorio, Ahora, TimeSpan.FromMinutes(15));

        Assert.True(File.Exists(ruta));
        Assert.Single(repo.Corridas); // no se agregó una segunda fila para el mismo archivo
    }

    [Fact]
    public async Task ReconciliarDumpHuerfanosAsync_IgnoraArchivosTmp()
    {
        var directorio = CrearDirectorioTemporal();
        var ruta = Path.Combine(directorio, "en-progreso.tmp");
        File.WriteAllBytes(ruta, new byte[] { 1 });
        File.SetLastWriteTimeUtc(ruta, Ahora.AddDays(-30));
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);

        // El glob de este barrido es *.dump -- un .tmp sin fila lo maneja LimpiarTmpHuerfanos,
        // no este método.
        await svc.ReconciliarDumpHuerfanosAsync(directorio, Ahora, TimeSpan.FromMinutes(15));

        Assert.True(File.Exists(ruta));
    }

    [Fact]
    public async Task ReconciliarDumpHuerfanosAsync_DirectorioInexistente_NoLanzaYNoCreaElDirectorio()
    {
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance);
        var directorioInexistente = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid());

        await svc.ReconciliarDumpHuerfanosAsync(directorioInexistente, Ahora);

        Assert.False(Directory.Exists(directorioInexistente));
    }

    private sealed class NotificadorAlertasFake : INotificadorAlertas
    {
        private readonly bool _explota;
        public List<CorridaBackup> Notificadas { get; } = new();

        public NotificadorAlertasFake(bool explota = false) => _explota = explota;

        public Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
        {
            Notificadas.Add(corrida);
            if (_explota)
                throw new InvalidOperationException("el notificador explotó");
            return Task.CompletedTask;
        }

        public Task<ResultadoPruebaAlertaDto> ProbarPingAsync(string url, CancellationToken ct = default)
            => Task.FromResult(new ResultadoPruebaAlertaDto(true, 200, "ok"));
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Exitosa_NotificaLaCorrida()
    {
        var directorio = CrearDirectorioTemporal();
        var notificador = new NotificadorAlertasFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(),
            notificador, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Exitosa, notificada.Resultado);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Fallida_NotificaLaCorrida()
    {
        var directorio = CrearDirectorioTemporal();
        var notificador = new NotificadorAlertasFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: false, mensajeError: "pg_dump: fallo simulado"),
            new CorridaBackupRepositoryFake(), notificador, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
        Assert.Equal("pg_dump: fallo simulado", notificada.MotivoFallo);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_ElNotificadorExplota_NoRompeElBackupNiPierdeLaCorrida()
    {
        // El test más importante del conjunto: notificar es best-effort. Un canal de alerta roto
        // que además tumba el backup convierte una molestia en un desastre.
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), repo,
            new NotificadorAlertasFake(explota: true), NullLogger<ServicioBackup>.Instance);

        var ex = await Record.ExceptionAsync(() =>
            svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None));

        Assert.Null(ex);
        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Exitosa, corrida.Resultado);
    }
}
