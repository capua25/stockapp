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
/// Guardián de la migración AgregaCajaYFuenteAlPago: agrega la caja a fuentes, ingresos y pagos,
/// copia la fuente del gasto a cada uno de sus pagos (antes de que el gasto deje de tenerla) y
/// clasifica como POA los literales B y C. Siembra con SQL crudo en la migración anterior porque
/// el modelo actual ya no coincide con ese esquema. Base nueva en el contenedor compartido.
/// </summary>
[Collection("Postgres")]
public sealed class CajaYFuenteAlPagoMigrationTests
{
    private const string MigracionAnterior = "20260910225922_AgregaIndiceFuncionalNombreCatalogos";

    private readonly PostgresFixture _fixture;

    public CajaYFuenteAlPagoMigrationTests(PostgresFixture fixture) => _fixture = fixture;

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

    private static string BuscarMigracion(string connectionString)
    {
        using var ctx = CrearContexto(connectionString);
        return ctx.Database.GetMigrations().Single(m => m.EndsWith("_AgregaCajaYFuenteAlPago"));
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

    private static async Task<int> FuentePorNombreAsync(NpgsqlConnection con, string nombre)
        => await InsertarAsync(con, $"INSERT INTO \"FuentesFinanciamiento\" (\"Nombre\") VALUES ('{nombre}') RETURNING \"Id\"");

    private static Task<int> GastoAsync(NpgsqlConnection con, int fuente, int proveedor, int rubro, int condicion)
        => InsertarAsync(con,
            "INSERT INTO \"Gastos\" (\"Detalle\", \"Fecha\", \"MontoTotal\", \"FuenteFinanciamientoId\", \"ProveedorId\", " +
            $"\"RubroGastoId\", \"CondicionPago\") VALUES ('g', now(), 1000, {fuente}, {proveedor}, {rubro}, {condicion}) RETURNING \"Id\"");

    private static async Task PagoAsync(NpgsqlConnection con, int gasto, bool automatico, bool activo)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"PagosGasto\" (\"GastoId\", \"Fecha\", \"Monto\", \"EsAutomatico\", \"Activo\") " +
            $"VALUES ({gasto}, now(), 100, {automatico}, {activo})", con);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(int a, int b, int c, int multas, int[] gastos)> SembrarAsync(
        NpgsqlConnection con, bool conBYC)
    {
        var proveedor = await InsertarAsync(con, "INSERT INTO \"Proveedores\" (\"Nombre\") VALUES ('P') RETURNING \"Id\"");
        var rubro = await InsertarAsync(con, "INSERT INTO \"RubrosGasto\" (\"Codigo\", \"Nombre\") VALUES (1, 'R') RETURNING \"Id\"");
        var a = await FuentePorNombreAsync(con, conBYC ? "Literal A" : "A");
        var b = conBYC ? await FuentePorNombreAsync(con, " literal  b ") : 0;
        var c = conBYC ? await FuentePorNombreAsync(con, "C") : 0;
        var multas = await FuentePorNombreAsync(con, "Multas");

        if (!conBYC) return (a, b, c, multas, Array.Empty<int>());

        var g1 = await GastoAsync(con, a, proveedor, rubro, condicion: 0);
        await PagoAsync(con, g1, automatico: true, activo: true);
        var g2 = await GastoAsync(con, multas, proveedor, rubro, condicion: 1);
        await PagoAsync(con, g2, automatico: false, activo: true);
        await PagoAsync(con, g2, automatico: false, activo: false);
        var g3 = await GastoAsync(con, b, proveedor, rubro, condicion: 1);
        await PagoAsync(con, g3, automatico: false, activo: true);
        var g4 = await GastoAsync(con, a, proveedor, rubro, condicion: 1);
        await InsertarAsync(con,
            $"INSERT INTO \"IngresosCaja\" (\"Fecha\", \"Concepto\", \"FuenteFinanciamientoId\", \"Monto\") VALUES (now(), 'i', {c}, 50) RETURNING \"Id\"");
        return (a, b, c, multas, new[] { g1, g2, g3, g4 });
    }

    [Fact]
    public async Task Migracion_CopiaLaFuenteDelGastoACadaPago_YDejaTodoEnLocal()
    {
        var cs = await CrearBaseVaciaAsync();
        await MigrarAsync(cs, MigracionAnterior);
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            await SembrarAsync(con, conBYC: true);
        }

        await MigrarAsync(cs, BuscarMigracion(cs));

        await using var c2 = new NpgsqlConnection(cs);
        await c2.OpenAsync();
        Assert.Equal(4, await ContarAsync(c2, "SELECT count(*) FROM \"PagosGasto\""));
        Assert.Equal(0, await ContarAsync(c2,
            "SELECT count(*) FROM \"PagosGasto\" p JOIN \"Gastos\" g ON g.\"Id\" = p.\"GastoId\" " +
            "WHERE p.\"FuenteFinanciamientoId\" <> g.\"FuenteFinanciamientoId\""));
        Assert.Equal(0, await ContarAsync(c2, "SELECT count(*) FROM \"PagosGasto\" WHERE \"Caja\" <> 0"));
        Assert.Equal(0, await ContarAsync(c2, "SELECT count(*) FROM \"IngresosCaja\" WHERE \"Caja\" <> 0"));
        Assert.Equal(0, await ContarAsync(c2, "SELECT count(*) FROM \"FuentesFinanciamiento\" WHERE \"Nombre\" IN ('Literal A', 'Multas') AND \"Caja\" <> 0"));
        Assert.Equal(1, await ContarAsync(c2, "SELECT count(*) FROM \"FuentesFinanciamiento\" WHERE \"Nombre\" = ' literal  b ' AND \"Caja\" = 1"));
        Assert.Equal(1, await ContarAsync(c2, "SELECT count(*) FROM \"FuentesFinanciamiento\" WHERE \"Nombre\" = 'C' AND \"Caja\" = 1"));
        Assert.Equal(1, await ContarAsync(c2,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'PagosGasto' " +
            "AND column_name = 'FuenteFinanciamientoId' AND is_nullable = 'NO'"));
        Assert.Equal(1, await ContarAsync(c2, "SELECT count(*) FROM (SELECT to_regclass('\"LotesImportacion\"') AS r) x WHERE r IS NOT NULL"));

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO \"PagosGasto\" (\"GastoId\", \"Fecha\", \"Monto\", \"EsAutomatico\", \"Caja\", \"FuenteFinanciamientoId\") " +
                "SELECT \"Id\", now(), 1, false, 0, 999999 FROM \"Gastos\" LIMIT 1", c2);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23503", ex.SqlState);
    }

    [Fact]
    public async Task Migracion_SinFuentesByC_NoFallaYTodasQuedanLocal()
    {
        var cs = await CrearBaseVaciaAsync();
        await MigrarAsync(cs, MigracionAnterior);
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            await SembrarAsync(con, conBYC: false);
        }

        await MigrarAsync(cs, BuscarMigracion(cs));

        await using var c2 = new NpgsqlConnection(cs);
        await c2.OpenAsync();
        Assert.Equal(2, await ContarAsync(c2, "SELECT count(*) FROM \"FuentesFinanciamiento\" WHERE \"Caja\" = 0"));
        Assert.Equal(0, await ContarAsync(c2, "SELECT count(*) FROM \"FuentesFinanciamiento\" WHERE \"Caja\" <> 0"));
    }

    [Fact]
    public async Task Down_QuitaLasColumnasNuevas_SinTocarLaFuenteDelGasto()
    {
        var cs = await CrearBaseVaciaAsync();
        await MigrarAsync(cs, MigracionAnterior);
        int[] gastos;
        int[] fuentes;
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            var s = await SembrarAsync(con, conBYC: true);
            gastos = s.gastos;
            fuentes = new[] { s.a, s.multas, s.b, s.a };
        }

        await MigrarAsync(cs, BuscarMigracion(cs));
        await MigrarAsync(cs, MigracionAnterior);

        await using var c2 = new NpgsqlConnection(cs);
        await c2.OpenAsync();
        Assert.Equal(0, await ContarAsync(c2,
            "SELECT count(*) FROM information_schema.columns WHERE " +
            "(table_name = 'PagosGasto' AND column_name IN ('Caja', 'FuenteFinanciamientoId')) " +
            "OR (table_name IN ('IngresosCaja', 'FuentesFinanciamiento') AND column_name = 'Caja')"));
        for (var i = 0; i < gastos.Length; i++)
            Assert.Equal(1, await ContarAsync(c2,
                $"SELECT count(*) FROM \"Gastos\" WHERE \"Id\" = {gastos[i]} AND \"FuenteFinanciamientoId\" = {fuentes[i]}"));
    }
}
