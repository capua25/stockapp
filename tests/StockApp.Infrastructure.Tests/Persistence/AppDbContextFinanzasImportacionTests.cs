// tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextFinanzasImportacionTests.cs
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// F5c Task 1: la columna IdImportacion (Guid?, nullable) tiene que existir y persistir en
/// Gasto, IngresoCaja, LineaPoa y PagoGasto — es la base de la trazabilidad que
/// ImportacionRepository estampa al escribir y que usa para encontrar qué revertir.
///
/// fix/integridad-referencial: desde AgregaFksLotesImportacion, esa columna es además una FK
/// real hacia LotesImportacion.Id (Restrict, nullable). Los tests *_ViolaLaFk de abajo prueban
/// que la BASE rechaza un IdImportacion que no corresponde a ningún lote — mismo patrón que
/// TareaRepositoryTests.AgregarAsync_ConCreadaPorUsuarioIdInexistente_ViolaLaFk.
/// </summary>
public class AppDbContextFinanzasImportacionTests : PostgresRepositoryTestBase
{
    public AppDbContextFinanzasImportacionTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> SembrarLoteAsync(int ejercicio = 2026)
    {
        var usuario = new Usuario
        {
            NombreUsuario = $"lote-seed-{Guid.NewGuid():N}",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var lote = new LoteImportacion
        {
            Id = Guid.NewGuid(),
            Fecha = DateTime.UtcNow,
            UsuarioId = usuario.Id,
            Ejercicio = ejercicio,
        };
        Context.LotesImportacion.Add(lote);
        await Context.SaveChangesAsync();
        return lote.Id;
    }

    [Fact]
    public async Task Gasto_IdImportacion_PersisteYSePuedeConsultarPorIgualdad()
    {
        var idLote = await SembrarLoteAsync();
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
    public async Task Gasto_ConIdImportacionInexistente_ViolaLaFk()
    {
        var proveedor = new Proveedor { Nombre = "ACME SA" };
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        var rubro = new RubroGasto { Codigo = 1, Nombre = "Paseos Públicos" };
        Context.Proveedores.Add(proveedor);
        Context.FuentesFinanciamiento.Add(fuente);
        Context.RubrosGasto.Add(rubro);
        Context.Gastos.Add(new Gasto
        {
            Proveedor = proveedor, FuenteFinanciamiento = fuente, RubroGasto = rubro,
            Detalle = "Gasto con lote fantasma", Fecha = DateTime.UtcNow, MontoTotal = 100m,
            IdImportacion = Guid.NewGuid(), // no existe ningún LoteImportacion con este Id
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }

    [Fact]
    public async Task IngresoCaja_IdImportacion_PersisteYAceptaNull()
    {
        var idLote = await SembrarLoteAsync();
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        Context.FuentesFinanciamiento.Add(fuente);
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
    public async Task IngresoCaja_ConIdImportacionInexistente_ViolaLaFk()
    {
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        Context.FuentesFinanciamiento.Add(fuente);
        Context.IngresosCaja.Add(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Saldo con lote fantasma", Monto = 1000m,
            FuenteFinanciamiento = fuente, IdImportacion = Guid.NewGuid(),
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }

    [Fact]
    public async Task LineaPoa_IdImportacion_PersisteYAceptaNull()
    {
        var idLote = await SembrarLoteAsync();
        Context.LineasPoa.Add(new LineaPoa
        {
            Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, IdImportacion = idLote,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrada = Context.LineasPoa.Single(l => l.IdImportacion == idLote);
        Assert.Equal("COMPOSTERAS", encontrada.Nombre);
    }

    [Fact]
    public async Task LineaPoa_ConIdImportacionInexistente_ViolaLaFk()
    {
        Context.LineasPoa.Add(new LineaPoa
        {
            Nombre = "LOTE FANTASMA", Programa = "Ambiente", Ejercicio = 2026,
            IdImportacion = Guid.NewGuid(),
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }

    [Fact]
    public async Task PagoGasto_ConIdImportacionInexistente_ViolaLaFk()
    {
        var proveedor = new Proveedor { Nombre = "ACME SA" };
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        var rubro = new RubroGasto { Codigo = 1, Nombre = "Paseos Públicos" };
        Context.Proveedores.Add(proveedor);
        Context.FuentesFinanciamiento.Add(fuente);
        Context.RubrosGasto.Add(rubro);
        var gasto = new Gasto
        {
            Proveedor = proveedor, FuenteFinanciamiento = fuente, RubroGasto = rubro,
            Detalle = "Gasto para el pago", Fecha = DateTime.UtcNow, MontoTotal = 100m,
        };
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.PagosGasto.Add(new PagoGasto
        {
            GastoId = gasto.Id, Fecha = DateTime.UtcNow, Monto = 100m,
            IdImportacion = Guid.NewGuid(),
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }
}
