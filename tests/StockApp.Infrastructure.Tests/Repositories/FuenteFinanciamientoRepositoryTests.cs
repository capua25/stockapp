using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class FuenteFinanciamientoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly FuenteFinanciamientoRepository _repo;

    public FuenteFinanciamientoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new FuenteFinanciamientoRepository(Context);
    }

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal B" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Literal B", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasOrdenadasPorNombre_SinFiltroDeActivo()
    {
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Multas" });
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal A", Activo = false });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Literal A", result[0].Nombre);
        Assert.Equal("Multas", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal C" });

        Assert.True(await _repo.ExisteNombreAsync("Literal C"));
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaFuente_RetornaFalse()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal C" });

        Assert.False(await _repo.ExisteNombreAsync("Literal C", excluyendoId: id));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_Persiste()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Excedentes" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    private async Task<(int fuenteId, int proveedorId, int rubroId)> SeedFuenteConMaestrosAsync()
    {
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro test" };
        Context.AddRange(fuente, proveedor, rubro);
        await Context.SaveChangesAsync();
        return (fuente.Id, proveedor.Id, rubro.Id);
    }

    [Fact]
    public async Task EstaEnUsoAsync_SinMovimientos_False()
    {
        var (fuenteId, _, _) = await SeedFuenteConMaestrosAsync();

        Assert.False(await _repo.EstaEnUsoAsync(fuenteId));
    }

    [Fact]
    public async Task EstaEnUsoAsync_ConIngresoDadoDeBaja_True()
    {
        var (fuenteId, _, _) = await SeedFuenteConMaestrosAsync();
        Context.Add(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Baja", FuenteFinanciamientoId = fuenteId, Monto = 1m, Activo = false,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Assert.True(await _repo.EstaEnUsoAsync(fuenteId));
    }

    [Fact]
    public async Task EstaEnUsoAsync_ConPagoAnulado_True()
    {
        var (fuenteId, proveedorId, rubroId) = await SeedFuenteConMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, Detalle = "Gasto de prueba", Fecha = DateTime.UtcNow, MontoTotal = 1000m,
            RubroGastoId = rubroId, CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(30),
        };
        gasto.Pagos.Add(new PagoGasto
        {
            Fecha = DateTime.UtcNow, Monto = 100m, Activo = false, FuenteFinanciamientoId = fuenteId,
        });
        Context.Add(gasto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Assert.True(await _repo.EstaEnUsoAsync(fuenteId));
    }
}
