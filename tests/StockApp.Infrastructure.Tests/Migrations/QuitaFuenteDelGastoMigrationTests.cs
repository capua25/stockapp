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
/// Guardián de la migración QuitaFuenteDelGasto (el contract): el gasto deja de tener fuente porque cada pago
/// ya guarda la suya; el Down recrea la columna nullable y la repuebla desde el pago automático o, si no hay,
/// desde el primer pago. Siembra con SQL crudo en la migración anterior porque el modelo actual ya no coincide
/// con ese esquema. Base nueva en el contenedor compartido.
/// </summary>
[Collection("Postgres")]
public sealed class QuitaFuenteDelGastoMigrationTests
{
    private readonly PostgresFixture _fixture;

    public QuitaFuenteDelGastoMigrationTests(PostgresFixture fixture) => _fixture = fixture;

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

    private static Task<int> FuenteAsync(NpgsqlConnection con, string nombre)
        => InsertarAsync(con, $"INSERT INTO \"FuentesFinanciamiento\" (\"Nombre\") VALUES ('{nombre}') RETURNING \"Id\"");

    private static Task<int> GastoAsync(NpgsqlConnection con, int fuente, int proveedor, int rubro, int condicion)
        => InsertarAsync(con,
            "INSERT INTO \"Gastos\" (\"Detalle\", \"Fecha\", \"MontoTotal\", \"FuenteFinanciamientoId\", \"ProveedorId\", " +
            $"\"RubroGastoId\", \"CondicionPago\") VALUES ('g', now(), 1000, {fuente}, {proveedor}, {rubro}, {condicion}) RETURNING \"Id\"");

    private static async Task PagoAsync(NpgsqlConnection con, int gasto, int fuente, bool automatico)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"PagosGasto\" (\"GastoId\", \"Fecha\", \"Monto\", \"EsAutomatico\", \"Activo\", \"FuenteFinanciamientoId\") " +
            $"VALUES ({gasto}, now(), 100, {automatico}, true, {fuente})", con);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Contado (pago manual de Multas con Id menor, luego el automático de A), crédito (Multas y A) y un gasto sin pagos.</summary>
    private static async Task<(int a, int multas, int contado, int credito, int sinPagos)> SembrarAsync(NpgsqlConnection con)
    {
        var proveedor = await InsertarAsync(con, "INSERT INTO \"Proveedores\" (\"Nombre\") VALUES ('P') RETURNING \"Id\"");
        var rubro = await InsertarAsync(con, "INSERT INTO \"RubrosGasto\" (\"Codigo\", \"Nombre\") VALUES (1, 'R') RETURNING \"Id\"");
        var a = await FuenteAsync(con, "A");
        var multas = await FuenteAsync(con, "Multas");

        var contado = await GastoAsync(con, a, proveedor, rubro, condicion: 0);
        await PagoAsync(con, contado, multas, automatico: false);
        await PagoAsync(con, contado, a, automatico: true);
        var credito = await GastoAsync(con, multas, proveedor, rubro, condicion: 1);
        await PagoAsync(con, credito, multas, automatico: false);
        await PagoAsync(con, credito, a, automatico: false);
        var sinPagos = await GastoAsync(con, a, proveedor, rubro, condicion: 1);
        return (a, multas, contado, credito, sinPagos);
    }

    [Fact]
    public async Task Up_QuitaLaColumna_YLosPagosConservanSuFuente()
    {
        var cs = await CrearBaseVaciaAsync();
        var antes = BuscarMigracion(cs, "AgregaCajaYFuenteAlPago");
        await MigrarAsync(cs, antes);
        (int a, int multas, int contado, int credito, int sinPagos) s;
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            s = await SembrarAsync(con);
        }

        await MigrarAsync(cs, BuscarMigracion(cs, "QuitaFuenteDelGasto"));

        await using var c2 = new NpgsqlConnection(cs);
        await c2.OpenAsync();
        Assert.Equal(0, await ContarAsync(c2,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Gastos' " +
            "AND column_name = 'FuenteFinanciamientoId'"));
        Assert.Equal(4, await ContarAsync(c2, "SELECT count(*) FROM \"PagosGasto\""));
        Assert.Equal(2, await ContarAsync(c2,
            $"SELECT count(*) FROM \"PagosGasto\" WHERE \"GastoId\" = {s.contado} AND \"FuenteFinanciamientoId\" IN ({s.a}, {s.multas})"));
        Assert.Equal(1, await ContarAsync(c2,
            $"SELECT count(*) FROM \"PagosGasto\" WHERE \"GastoId\" = {s.credito} AND \"FuenteFinanciamientoId\" = {s.multas}"));
        Assert.Equal(1, await ContarAsync(c2,
            $"SELECT count(*) FROM \"PagosGasto\" WHERE \"GastoId\" = {s.credito} AND \"FuenteFinanciamientoId\" = {s.a}"));
        Assert.Equal(1, await ContarAsync(c2, "SELECT count(*) FROM (SELECT to_regclass('\"LotesImportacion\"') AS r) x WHERE r IS NOT NULL"));
    }

    [Fact]
    public async Task Down_RepueblaDesdeElPagoAutomaticoOElPrimerPago_YElGastoSinPagosQuedaNull()
    {
        var cs = await CrearBaseVaciaAsync();
        var antes = BuscarMigracion(cs, "AgregaCajaYFuenteAlPago");
        await MigrarAsync(cs, antes);
        (int a, int multas, int contado, int credito, int sinPagos) s;
        await using (var con = new NpgsqlConnection(cs))
        {
            await con.OpenAsync();
            s = await SembrarAsync(con);
        }

        await MigrarAsync(cs, BuscarMigracion(cs, "QuitaFuenteDelGasto"));
        await MigrarAsync(cs, antes);

        await using var c2 = new NpgsqlConnection(cs);
        await c2.OpenAsync();
        Assert.Equal(1, await ContarAsync(c2,
            $"SELECT count(*) FROM \"Gastos\" WHERE \"Id\" = {s.contado} AND \"FuenteFinanciamientoId\" = {s.a}"));
        Assert.Equal(1, await ContarAsync(c2,
            $"SELECT count(*) FROM \"Gastos\" WHERE \"Id\" = {s.credito} AND \"FuenteFinanciamientoId\" = {s.multas}"));
        Assert.Equal(1, await ContarAsync(c2,
            $"SELECT count(*) FROM \"Gastos\" WHERE \"Id\" = {s.sinPagos} AND \"FuenteFinanciamientoId\" IS NULL"));
        Assert.Equal(1, await ContarAsync(c2,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Gastos' " +
            "AND column_name = 'FuenteFinanciamientoId' AND is_nullable = 'YES'"));
    }
}
