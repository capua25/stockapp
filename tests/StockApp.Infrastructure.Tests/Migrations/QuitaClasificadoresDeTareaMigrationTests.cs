using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Migrations;

/// <summary>
/// Guardián de la migración destructiva QuitaClasificadoresDeTarea (spec 2026-10-30): la tarea pierde zona,
/// dimensión temática, organismo responsable y origen de financiamiento (sus valores se borran), pero conserva
/// expediente, proyecto, notas, estado y trazabilidad. Los catálogos no se tocan: pasan a usarlos los proyectos.
/// Siembra con SQL crudo en AgregaProyectos porque el modelo actual ya no tiene esas columnas. Base nueva en el
/// contenedor compartido.
/// </summary>
[Collection("Postgres")]
public sealed class QuitaClasificadoresDeTareaMigrationTests
{
    private static readonly string[] ColumnasQuitadas =
        { "ZonaId", "DimensionTematicaId", "OrganismoResponsableId", "OrigenFinanciamientoId" };

    private readonly PostgresFixture _fixture;

    public QuitaClasificadoresDeTareaMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    private async Task<string> CrearBaseVaciaAsync()
    {
        var nombreBase = "migtest_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{nombreBase}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = nombreBase }.ConnectionString;
    }

    private static AppDbContext CrearContexto(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private static async Task MigrarAsync(string connectionString, string destino)
    {
        await using var ctx = CrearContexto(connectionString);
        await ctx.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(destino);
    }

    private static string BuscarMigracion(string connectionString, string sufijo)
    {
        using var ctx = CrearContexto(connectionString);
        return ctx.Database.GetMigrations().Single(m => m.EndsWith("_" + sufijo));
    }

    private static async Task<int> InsertarAsync(NpgsqlConnection con, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, con);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> ContarAsync(NpgsqlConnection con, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, con);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<long> ContarCatalogosAsync(NpgsqlConnection con)
        => ContarAsync(con,
            "SELECT (SELECT count(*) FROM \"Zonas\") + (SELECT count(*) FROM \"DimensionesTematicas\") + " +
            "(SELECT count(*) FROM \"OrganismosResponsables\") + (SELECT count(*) FROM \"OrigenesFinanciamiento\")");

    private sealed record Sembrado(
        string Cs, int Tarea, int Terminada, int Proyecto, int Operador, int Documento, long CatalogosAntes);

    /// <summary>Migra hasta AgregaProyectos, siembra una tarea EnCurso con los 4 clasificadores, expediente,
    /// 2 notas, TomadaPor y FechaInicio, y migra a QuitaClasificadoresDeTarea.</summary>
    private async Task<Sembrado> MigrarConDatosAsync()
    {
        var cs = await CrearBaseVaciaAsync();
        await MigrarAsync(cs, BuscarMigracion(cs, "AgregaProyectos"));
        int tarea, terminada, proyecto, operador, documento;
        long catalogosAntes;
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            operador = await InsertarAsync(con,
                "INSERT INTO \"Usuarios\" (\"NombreUsuario\", \"HashContrasena\", \"Rol\", \"Activo\", \"FechaAlta\") " +
                "VALUES ('op', 'x', 1, true, now()) RETURNING \"Id\"");
            var zona = await InsertarAsync(con, "INSERT INTO \"Zonas\" (\"Nombre\") VALUES ('Centro') RETURNING \"Id\"");
            var dimension = await InsertarAsync(con,
                "INSERT INTO \"DimensionesTematicas\" (\"Nombre\") VALUES ('Vialidad') RETURNING \"Id\"");
            var organismo = await InsertarAsync(con,
                "INSERT INTO \"OrganismosResponsables\" (\"Nombre\") VALUES ('Intendencia') RETURNING \"Id\"");
            var origen = await InsertarAsync(con,
                "INSERT INTO \"OrigenesFinanciamiento\" (\"Nombre\") VALUES ('FIGM') RETURNING \"Id\"");
            documento = await InsertarAsync(con,
                "INSERT INTO \"DocumentosAdministrativos\" (\"Tipo\", \"Numero\", \"Anio\", \"Descripcion\", \"Estado\", " +
                "\"FechaEmision\", \"FechaRegistro\", \"RegistradoPorUsuarioId\") " +
                $"VALUES (0, '0099', 2026, 'exp', 0, now(), now(), {operador}) RETURNING \"Id\"");
            tarea = await InsertarAsync(con,
                "INSERT INTO \"Tareas\" (\"Titulo\", \"Estado\", \"Prioridad\", \"CreadaPorUsuarioId\", \"FechaCreacion\", " +
                "\"TomadaPorUsuarioId\", \"FechaInicio\", \"ZonaId\", \"DimensionTematicaId\", \"OrganismoResponsableId\", " +
                "\"OrigenFinanciamientoId\", \"DocumentoAdministrativoId\") " +
                $"VALUES ('Bacheo', 1, 2, {operador}, now(), {operador}, '2026-10-01 12:30:00+00', {zona}, {dimension}, " +
                $"{organismo}, {origen}, {documento}) RETURNING \"Id\"");
            proyecto = await InsertarAsync(con,
                "INSERT INTO \"Proyectos\" (\"Titulo\", \"Estado\", \"Prioridad\", \"CreadoPorUsuarioId\", \"FechaCreacion\") " +
                $"VALUES ('Plaza', 1, 1, {operador}, now()) RETURNING \"Id\"");
            terminada = await InsertarAsync(con,
                "INSERT INTO \"Tareas\" (\"Titulo\", \"Descripcion\", \"Estado\", \"Prioridad\", \"FechaLimite\", " +
                "\"CreadaPorUsuarioId\", \"FechaCreacion\", \"TomadaPorUsuarioId\", \"FechaInicio\", " +
                "\"CerradaPorUsuarioId\", \"FechaFin\", \"ZonaId\", \"DimensionTematicaId\", \"OrganismoResponsableId\", " +
                "\"OrigenFinanciamientoId\", \"ProyectoId\") " +
                "VALUES ('Poda', 'Lado norte', 2, 0, '2026-11-15 00:00:00+00', " +
                $"{operador}, '2026-09-01 08:00:00+00', {operador}, '2026-09-02 09:00:00+00', " +
                $"{operador}, '2026-09-03 10:00:00+00', {zona}, {dimension}, {organismo}, {origen}, {proyecto}) RETURNING \"Id\"");
            for (var i = 1; i <= 2; i++)
                await InsertarAsync(con,
                    "INSERT INTO \"NotasTarea\" (\"TareaId\", \"UsuarioId\", \"Fecha\", \"Texto\", \"EsAutomatica\") " +
                    $"VALUES ({tarea}, {operador}, now(), 'nota {i}', false) RETURNING \"Id\"");
            catalogosAntes = await ContarCatalogosAsync(con);
        }

        await MigrarAsync(cs, BuscarMigracion(cs, "QuitaClasificadoresDeTarea"));
        return new Sembrado(cs, tarea, terminada, proyecto, operador, documento, catalogosAntes);
    }

    [Fact]
    public async Task Up_TareaConExpedienteYNotas_ConservaTodoMenosClasificadores()
    {
        var s = await MigrarConDatosAsync();
        await using var con = new NpgsqlConnection(s.Cs);
        await con.OpenAsync();

        foreach (var columna in ColumnasQuitadas)
            Assert.Equal(0, await ContarAsync(con,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Tareas' " +
                $"AND column_name = '{columna}'"));

        Assert.Equal(1, await ContarAsync(con,
            $"SELECT count(*) FROM \"Tareas\" WHERE \"Id\" = {s.Tarea} AND \"Titulo\" = 'Bacheo' " +
            $"AND \"Estado\" = 1 AND \"Prioridad\" = 2 AND \"TomadaPorUsuarioId\" = {s.Operador} " +
            $"AND \"FechaInicio\" = '2026-10-01 12:30:00+00' AND \"DocumentoAdministrativoId\" = {s.Documento} " +
            "AND \"CerradaPorUsuarioId\" IS NULL AND \"FechaFin\" IS NULL AND \"ProyectoId\" IS NULL"));
        Assert.Equal(2, await ContarAsync(con,
            $"SELECT count(*) FROM \"NotasTarea\" WHERE \"TareaId\" = {s.Tarea} AND \"Texto\" IN ('nota 1', 'nota 2')"));
    }

    [Fact]
    public async Task Up_TareaTerminadaConProyecto_ConservaCierreFechasYProyecto()
    {
        var s = await MigrarConDatosAsync();
        await using var con = new NpgsqlConnection(s.Cs);
        await con.OpenAsync();

        Assert.Equal(1, await ContarAsync(con,
            $"SELECT count(*) FROM \"Tareas\" WHERE \"Id\" = {s.Terminada} AND \"Titulo\" = 'Poda' " +
            "AND \"Descripcion\" = 'Lado norte' AND \"Estado\" = 2 AND \"Prioridad\" = 0 " +
            "AND \"FechaLimite\" = '2026-11-15 00:00:00+00' AND \"FechaCreacion\" = '2026-09-01 08:00:00+00' " +
            $"AND \"TomadaPorUsuarioId\" = {s.Operador} AND \"FechaInicio\" = '2026-09-02 09:00:00+00' " +
            $"AND \"CerradaPorUsuarioId\" = {s.Operador} AND \"FechaFin\" = '2026-09-03 10:00:00+00' " +
            $"AND \"ProyectoId\" = {s.Proyecto} AND \"DocumentoAdministrativoId\" IS NULL"));
    }

    [Fact]
    public async Task Up_CatalogosIntactos()
    {
        var s = await MigrarConDatosAsync();
        await using var con = new NpgsqlConnection(s.Cs);
        await con.OpenAsync();

        Assert.Equal(s.CatalogosAntes, await ContarCatalogosAsync(con));
        Assert.Equal(1, await ContarAsync(con, "SELECT count(*) FROM \"Zonas\" WHERE \"Nombre\" = 'Centro'"));
        Assert.Equal(1, await ContarAsync(con, "SELECT count(*) FROM \"DimensionesTematicas\" WHERE \"Nombre\" = 'Vialidad'"));
        Assert.Equal(1, await ContarAsync(con, "SELECT count(*) FROM \"OrganismosResponsables\" WHERE \"Nombre\" = 'Intendencia'"));
        Assert.Equal(1, await ContarAsync(con, "SELECT count(*) FROM \"OrigenesFinanciamiento\" WHERE \"Nombre\" = 'FIGM'"));
    }

    [Fact]
    public async Task Down_RecreaLasColumnasVacias()
    {
        var s = await MigrarConDatosAsync();
        await MigrarAsync(s.Cs, BuscarMigracion(s.Cs, "AgregaProyectos"));
        await using var con = new NpgsqlConnection(s.Cs);
        await con.OpenAsync();

        foreach (var columna in ColumnasQuitadas)
        {
            Assert.Equal(1, await ContarAsync(con,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Tareas' " +
                $"AND column_name = '{columna}' AND is_nullable = 'YES'"));
            Assert.Equal(1, await ContarAsync(con,
                $"SELECT count(*) FROM \"Tareas\" WHERE \"Id\" = {s.Tarea} AND \"{columna}\" IS NULL"));
            Assert.Equal(1, await ContarAsync(con,
                $"SELECT count(*) FROM pg_indexes WHERE tablename = 'Tareas' AND indexname = 'IX_Tareas_{columna}'"));
        }

        foreach (var fk in new[]
                 {
                     "FK_Tareas_Zonas_ZonaId", "FK_Tareas_DimensionesTematicas_DimensionTematicaId",
                     "FK_Tareas_OrganismosResponsables_OrganismoResponsableId",
                     "FK_Tareas_OrigenesFinanciamiento_OrigenFinanciamientoId",
                 })
            Assert.Equal(1, await ContarAsync(con,
                "SELECT count(*) FROM information_schema.table_constraints WHERE table_name = 'Tareas' " +
                $"AND constraint_type = 'FOREIGN KEY' AND constraint_name = '{fk}'"));
    }
}
