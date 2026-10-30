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
/// Guardián de la migración AgregaProyectos (aditiva): los Operadores preexistentes reciben
/// proyectos.ver y proyectos.gestionar, los Admin no, y las tareas y gastos existentes quedan
/// con ProyectoId null. Siembra con SQL crudo en la migración anterior.
/// </summary>
[Collection("Postgres")]
public sealed class AgregaProyectosMigrationTests
{
    private readonly PostgresFixture _fixture;

    public AgregaProyectosMigrationTests(PostgresFixture fixture) => _fixture = fixture;

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

    private static Task<int> UsuarioAsync(NpgsqlConnection con, string nombre, int rol)
        => InsertarAsync(con,
            "INSERT INTO \"Usuarios\" (\"NombreUsuario\", \"HashContrasena\", \"Rol\", \"Activo\", \"FechaAlta\") " +
            $"VALUES ('{nombre}', 'x', {rol}, true, now()) RETURNING \"Id\"");

    /// <summary>Migra hasta la anterior, siembra con SQL crudo y migra a AgregaProyectos.</summary>
    private async Task<(string cs, int operador, int admin, int tarea, int gasto)> MigrarConDatosAsync()
    {
        var cs = await CrearBaseVaciaAsync();
        await MigrarAsync(cs, BuscarMigracion(cs, "QuitaFuenteDelGasto"));
        int operador, admin, tarea, gasto;
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            operador = await UsuarioAsync(con, "op", 1);
            admin = await UsuarioAsync(con, "adm", 0);
            tarea = await InsertarAsync(con,
                "INSERT INTO \"Tareas\" (\"Titulo\", \"Estado\", \"Prioridad\", \"CreadaPorUsuarioId\", \"FechaCreacion\") " +
                $"VALUES ('t', 0, 1, {operador}, now()) RETURNING \"Id\"");
            var proveedor = await InsertarAsync(con, "INSERT INTO \"Proveedores\" (\"Nombre\") VALUES ('P') RETURNING \"Id\"");
            var rubro = await InsertarAsync(con, "INSERT INTO \"RubrosGasto\" (\"Codigo\", \"Nombre\") VALUES (1, 'R') RETURNING \"Id\"");
            gasto = await InsertarAsync(con,
                "INSERT INTO \"Gastos\" (\"Detalle\", \"Fecha\", \"MontoTotal\", \"ProveedorId\", \"RubroGastoId\", \"CondicionPago\") " +
                $"VALUES ('g', now(), 1000, {proveedor}, {rubro}, 1) RETURNING \"Id\"");
        }

        await MigrarAsync(cs, BuscarMigracion(cs, "AgregaProyectos"));
        return (cs, operador, admin, tarea, gasto);
    }

    [Fact]
    public async Task Up_OperadorExistente_RecibeVerYGestionarProyectos()
    {
        var s = await MigrarConDatosAsync();
        await using var con = new NpgsqlConnection(s.cs);
        await con.OpenAsync();

        Assert.Equal(2, await ContarAsync(con,
            $"SELECT count(*) FROM \"PermisosUsuario\" WHERE \"UsuarioId\" = {s.operador} " +
            "AND \"Permiso\" IN ('proyectos.ver', 'proyectos.gestionar')"));
    }

    [Fact]
    public async Task Up_AdminNoRecibeFilas()
    {
        var s = await MigrarConDatosAsync();
        await using var con = new NpgsqlConnection(s.cs);
        await con.OpenAsync();

        Assert.Equal(0, await ContarAsync(con, $"SELECT count(*) FROM \"PermisosUsuario\" WHERE \"UsuarioId\" = {s.admin}"));
    }

    [Fact]
    public async Task Up_TareasYGastosExistentes_QuedanConProyectoNull()
    {
        var s = await MigrarConDatosAsync();
        await using var con = new NpgsqlConnection(s.cs);
        await con.OpenAsync();

        Assert.Equal(1, await ContarAsync(con, $"SELECT count(*) FROM \"Tareas\" WHERE \"Id\" = {s.tarea} AND \"ProyectoId\" IS NULL"));
        Assert.Equal(1, await ContarAsync(con, $"SELECT count(*) FROM \"Gastos\" WHERE \"Id\" = {s.gasto} AND \"ProyectoId\" IS NULL"));
    }
}
