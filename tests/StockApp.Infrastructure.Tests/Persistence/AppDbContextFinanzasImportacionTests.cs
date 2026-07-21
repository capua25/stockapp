// tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextFinanzasImportacionTests.cs
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// F5c Task 1: la columna IdImportacion (Guid?, nullable, índice no-único) tiene que existir
/// y persistir en Gasto, IngresoCaja y LineaPoa — es la base de la trazabilidad que Task 4/5/6
/// estampan al escribir y que Task 8 usa para encontrar qué revertir.
/// </summary>
public class AppDbContextFinanzasImportacionTests : PostgresRepositoryTestBase
{
    public AppDbContextFinanzasImportacionTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Gasto_IdImportacion_PersisteYSePuedeConsultarPorIgualdad()
    {
        var idLote = Guid.NewGuid();
        var proveedor = new Proveedor { Nombre = "ACME SA" };
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        var rubro = new RubroGasto { Codigo = 1, Nombre = "Paseos Públicos" };
        Context.Proveedores.Add(proveedor);
        Context.FuentesFinanciamiento.Add(fuente);
        Context.RubrosGasto.Add(rubro);
        Context.Gastos.Add(new Gasto
        {
            Proveedor = proveedor, FuenteFinanciamiento = fuente, RubroGasto = rubro,
            Detalle = "Gasto importado", Fecha = DateTime.UtcNow, MontoTotal = 100m,
            IdImportacion = idLote,
        });
        Context.Gastos.Add(new Gasto
        {
            Proveedor = proveedor, FuenteFinanciamiento = fuente, RubroGasto = rubro,
            Detalle = "Gasto manual", Fecha = DateTime.UtcNow, MontoTotal = 50m,
            IdImportacion = null,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var delLote = Context.Gastos.Where(g => g.IdImportacion == idLote).ToList();
        var manuales = Context.Gastos.Where(g => g.IdImportacion == null).ToList();

        Assert.Single(delLote);
        Assert.Equal("Gasto importado", delLote[0].Detalle);
        Assert.Single(manuales);
    }

    [Fact]
    public async Task IngresoCaja_IdImportacion_PersisteYAceptaNull()
    {
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        Context.FuentesFinanciamiento.Add(fuente);
        var idLote = Guid.NewGuid();
        Context.IngresosCaja.Add(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Saldo inicial", Monto = 1000m,
            FuenteFinanciamiento = fuente, IdImportacion = idLote,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrado = Context.IngresosCaja.Single(i => i.IdImportacion == idLote);
        Assert.Equal("Saldo inicial", encontrado.Concepto);
    }

    [Fact]
    public async Task LineaPoa_IdImportacion_PersisteYAceptaNull()
    {
        var idLote = Guid.NewGuid();
        Context.LineasPoa.Add(new LineaPoa
        {
            Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, IdImportacion = idLote,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrada = Context.LineasPoa.Single(l => l.IdImportacion == idLote);
        Assert.Equal("COMPOSTERAS", encontrada.Nombre);
    }
}
