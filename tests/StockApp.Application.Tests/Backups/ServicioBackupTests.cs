using Microsoft.Extensions.Logging.Abstractions;
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
        var svc = new ServicioBackup(ejecutor, repo, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Exitosa, corrida.Resultado);
        Assert.NotNull(corrida.NombreArchivo);
        Assert.True(corrida.TamanioBytes > 0);
        Assert.Null(corrida.MotivoFallo);
        Assert.True(File.Exists(Path.Combine(directorio, corrida.NombreArchivo!)));
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
        var svc = new ServicioBackup(ejecutor, repo, NullLogger<ServicioBackup>.Instance);

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
        var svc = new ServicioBackup(ejecutor, repo, NullLogger<ServicioBackup>.Instance);

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

    [Fact]
    public void LimpiarTmpHuerfanos_BorraSoloArchivosTmp()
    {
        var directorio = CrearDirectorioTemporal();
        File.WriteAllBytes(Path.Combine(directorio, "huerfano1.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directorio, "huerfano2.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directorio, "backup_valido.dump"), new byte[] { 1 });
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), NullLogger<ServicioBackup>.Instance);

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
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), NullLogger<ServicioBackup>.Instance);
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

        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, NullLogger<ServicioBackup>.Instance);
        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        // +1 por la corrida de hoy, pero MENOS que antes porque la retención barrió las viejas.
        Assert.True(repo.Corridas.Count < cantidadAntes + 1);
        // Las filas borradas de la DB tampoco dejan archivo huérfano en disco.
        foreach (var nombreBorrado in Enumerable.Range(1, 90).Select(i => $"vieja_{i}.dump")
                     .Except(repo.Corridas.Select(c => c.NombreArchivo)))
        {
            Assert.False(File.Exists(Path.Combine(directorio, nombreBorrado)));
        }
    }

    [Fact]
    public async Task EjecutarCorridaAsync_TrasCorridaFallida_NoAplicaRetencion()
    {
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: false, mensajeError: "falló"), repo, NullLogger<ServicioBackup>.Instance);

        // No debe lanzar ni intentar listar/borrar nada más allá de agregar la corrida fallida.
        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        Assert.Single(repo.Corridas);
    }
}
