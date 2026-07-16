using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Test de concurrencia MANDATORY (I1 del review final f2-gastos): dos pagos simultáneos
/// sobre el mismo gasto que, juntos, superan el saldo pendiente. Con el check-then-insert
/// viejo (validar en memoria y recién ahí insertar) los dos podían pasar la validación
/// antes de que ninguno hubiera insertado su pago, sobrepagando la factura. Con
/// GastoRepository.RegistrarPagoAtomicoAsync (FOR UPDATE sobre la fila del gasto dentro de
/// la transacción) exactamente uno tiene éxito. Cada tarea usa su PROPIO AppDbContext (un
/// DbContext no es thread-safe). Requiere Docker (Testcontainers PostgreSQL).
/// </summary>
public class GastoRepositoryConcurrenciaTests : PostgresRepositoryTestBase
{
    private readonly GastoRepository _repo;

    public GastoRepositoryConcurrenciaTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new GastoRepository(Context);
    }

    [Fact]
    public async Task DosPagosSimultaneos_SuperanElSaldo_UnoSoloTieneExito()
    {
        // Seed: gasto de 1000, cada pago pide 600 → juntos (1200) superan el saldo
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro test" };
        Context.AddRange(proveedor, fuente, rubro);
        await Context.SaveChangesAsync();

        var gasto = new Gasto
        {
            ProveedorId = proveedor.Id,
            Detalle = "Gasto concurrente",
            Fecha = DateTime.UtcNow,
            MontoTotal = 1000m,
            FuenteFinanciamientoId = fuente.Id,
            RubroGastoId = rubro.Id,
            CondicionPago = CondicionPago.Credito,
            FechaVencimiento = DateTime.UtcNow.AddDays(30),
        };
        var gastoId = await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        async Task<bool> PagoAsync()
        {
            await using var ctx = Fixture.CrearContexto();
            var repo = new GastoRepository(ctx);
            var pago = new PagoGasto { GastoId = gastoId, Fecha = DateTime.UtcNow, Monto = 600m };
            try
            {
                await repo.RegistrarPagoAtomicoAsync(pago);
                return true;
            }
            catch (ReglaDeNegocioException)
            {
                return false;
            }
        }

        var resultados = await Task.WhenAll(PagoAsync(), PagoAsync());

        // Exactamente uno tiene éxito, el otro es rechazado por sobrepasar el saldo
        Assert.Equal(1, resultados.Count(r => r));
        Assert.Equal(1, resultados.Count(r => !r));

        await using var verify = Fixture.CrearContexto();
        var totalPagado = await verify.PagosGasto
            .Where(p => p.GastoId == gastoId && p.Activo)
            .SumAsync(p => (decimal?)p.Monto) ?? 0m;
        Assert.Equal(600m, totalPagado);   // nunca 1200 — sin sobrepago, sin lost-update
        Assert.Equal(1, await verify.PagosGasto.CountAsync(p => p.GastoId == gastoId));
    }
}
