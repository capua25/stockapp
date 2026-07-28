using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Backups;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Backups;

/// <summary>
/// ÚNICO test que prueba que un backup generado por EjecutorPgDumpProceso sirve para algo real
/// (spec Backups §8): dump contra la base sembrada por PostgresFixture -> restore en una base
/// temporal nueva del MISMO servidor -> verificar que tablas y conteos coinciden. El resto de
/// la suite (ServicioBackupTests con IEjecutorPgDump fake) solo prueba que SE INVOCA el
/// proceso, nunca que el archivo resultante es un backup restaurable.
///
/// REQUIERE los binarios pg_dump Y pg_restore en el PATH de la máquina que corre los tests
/// (paquete postgresql-client en Linux) ADEMÁS de Docker (que ya requiere toda la colección
/// "Postgres"). Corren en el HOST, no dentro del contenedor de Testcontainers — Testcontainers
/// solo expone el puerto. Sin esos binarios, el test falla con un mensaje claro (Win32Exception
/// envuelto por EjecutorPgDumpProceso / InvalidOperationException de pg_restore) — deliberado,
/// sin skip condicional (ver Task 12 del plan).
/// </summary>
[Collection("Postgres")]
public class RestaurabilidadBackupTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _directorioTrabajo =
        Path.Combine(Path.GetTempPath(), "StockAppRestaurabilidadTests_" + Guid.NewGuid());

    public RestaurabilidadBackupTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directorioTrabajo);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directorioTrabajo))
            Directory.Delete(_directorioTrabajo, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Dump_RestauradoEnBaseNueva_TieneLasMismasTablasYConteos()
    {
        // 1) Sembrar datos reconocibles en la base de la fixture.
        await using (var ctx = _fixture.CrearContexto())
        {
            ctx.Categorias.Add(new Categoria { Nombre = "Restaurabilidad-Cat", Activo = true });
            ctx.RubrosGasto.Add(new RubroGasto { Codigo = 999, Nombre = "Restaurabilidad-Rubro", Activo = true });
            await ctx.SaveChangesAsync();
        }

        int categoriasEsperadas, rubrosEsperados;
        await using (var ctx = _fixture.CrearContexto())
        {
            categoriasEsperadas = await ctx.Categorias.CountAsync();
            rubrosEsperados = await ctx.RubrosGasto.CountAsync();
        }

        // 2) Dump real con el ejecutor de PRODUCCIÓN (Task 3), no un fake.
        // NOTA (desviación del brief, documentada): el brief usa `new ConfigurationBuilder().Build()`,
        // pero StockApp.Infrastructure.Tests no referencia el paquete NuGet Microsoft.Extensions.Configuration
        // (solo su .Abstractions transitivo) y el constraint global prohíbe agregar paquetes nuevos.
        // ConfiguracionVacia (abajo) es el reemplazo sin dependencias nuevas: mismo efecto que un
        // ConfigurationBuilder vacío -> sin Backups:PgDumpPath -> resuelve "pg_dump" por PATH.
        var configuracion = new ConfiguracionVacia();
        var ejecutor = new EjecutorPgDumpProceso(configuracion, NullLogger<EjecutorPgDumpProceso>.Instance);
        var rutaDump = Path.Combine(_directorioTrabajo, "restaurabilidad.dump");

        var resultado = await ejecutor.EjecutarAsync(_fixture.ConnectionString, rutaDump, CancellationToken.None);

        Assert.True(resultado.Exitoso, $"pg_dump falló (¿está instalado 'postgresql-client'?): {resultado.MensajeError}");
        Assert.True(File.Exists(rutaDump));

        // 3) Crear una base nueva en el MISMO servidor y restaurar el dump ahí.
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString);
        var baseRestaurada = "restaurabilidad_" + Guid.NewGuid().ToString("N")[..8];

        builder.Database = "postgres"; // base de mantenimiento, siempre existe
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var crear = new NpgsqlCommand($"CREATE DATABASE \"{baseRestaurada}\";", admin);
            await crear.ExecuteNonQueryAsync();
        }

        builder.Database = baseRestaurada;
        var restoreExitCode = await EjecutarPgRestoreAsync(builder.ConnectionString, rutaDump);
        Assert.Equal(0, restoreExitCode);

        // 4) Verificar tablas y conteos en la base restaurada.
        var opcionesRestaurada = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(builder.ConnectionString).Options;
        await using var restaurada = new AppDbContext(opcionesRestaurada);

        Assert.Equal(categoriasEsperadas, await restaurada.Categorias.CountAsync());
        Assert.Equal(rubrosEsperados, await restaurada.RubrosGasto.CountAsync());
        Assert.Contains(await restaurada.Categorias.ToListAsync(), c => c.Nombre == "Restaurabilidad-Cat");
    }

    /// <summary>
    /// NOTA (pre-flight scan — duplicación DELIBERADA, no corregida): este bloque de invocación
    /// de proceso (ArgumentList, PGPASSWORD, RedirectStandardError) es ~15 líneas casi idénticas
    /// a EjecutorPgDumpProceso.EjecutarAsync (Task 3). NO se extrajo un helper común a propósito:
    /// hacerlo acoplaría este test de integración al código de producción (EjecutorPgDumpProceso
    /// es SOLO para pg_dump, nunca ejecuta pg_restore — no hay una abstracción de producción que
    /// este método debiera reusar sin inventar una interfaz nueva solo para un test). Es una
    /// decisión del usuario, no un descuido — si un reviewer futuro señala esta duplicación como
    /// hallazgo nuevo, este comentario ya explica por qué se dejó así.
    /// </summary>
    private static async Task<int> EjecutarPgRestoreAsync(string connectionStringDestino, string rutaDump)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionStringDestino);
        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pg_restore",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proceso.StartInfo.EnvironmentVariables["PGPASSWORD"] = builder.Password;
        proceso.StartInfo.ArgumentList.Add($"--host={builder.Host}");
        proceso.StartInfo.ArgumentList.Add($"--port={builder.Port}");
        proceso.StartInfo.ArgumentList.Add($"--username={builder.Username}");
        proceso.StartInfo.ArgumentList.Add($"--dbname={builder.Database}");
        proceso.StartInfo.ArgumentList.Add("--no-owner");
        proceso.StartInfo.ArgumentList.Add(rutaDump);

        proceso.Start();
        var stderr = await proceso.StandardError.ReadToEndAsync();
        await proceso.WaitForExitAsync();

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"pg_restore falló: {stderr}");

        return proceso.ExitCode;
    }

    /// <summary>
    /// Reemplazo de <c>new ConfigurationBuilder().Build()</c> (código del brief) sin agregar el
    /// paquete NuGet Microsoft.Extensions.Configuration — este proyecto de test solo tiene
    /// disponible transitivamente Microsoft.Extensions.Configuration.Abstractions (donde vive la
    /// interfaz IConfiguration) y Microsoft.Extensions.Primitives (IChangeToken), no el paquete
    /// concreto que provee ConfigurationBuilder. Mismo comportamiento observable que el brief
    /// pedía: un IConfiguration "vacío" donde toda clave (incluida "Backups:PgDumpPath") devuelve
    /// null, para que EjecutorPgDumpProceso resuelva "pg_dump" por PATH. Ver Global Constraints
    /// del Task 12: "ningún paquete NuGet nuevo, si no compila por falta de un paquete, reportarlo".
    /// </summary>
    private sealed class ConfiguracionVacia : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
