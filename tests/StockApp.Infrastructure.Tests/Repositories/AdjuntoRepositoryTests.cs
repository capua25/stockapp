using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

[Collection("Postgres")]
public class AdjuntoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly AdjuntoRepository _repo;

    public AdjuntoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new AdjuntoRepository(Context);
    }

    private async Task<int> CrearGastoAsync()
    {
        var proveedor = new Proveedor { Nombre = "Proveedor Test", Activo = true };
        Context.Proveedores.Add(proveedor);
        var fuente = new FuenteFinanciamiento { Nombre = "Fuente Test", Activo = true };
        Context.FuentesFinanciamiento.Add(fuente);
        var rubro = new RubroGasto { Nombre = "Rubro Test", Activo = true };
        Context.RubrosGasto.Add(rubro);
        await Context.SaveChangesAsync();

        var gasto = new Gasto
        {
            ProveedorId = proveedor.Id, Detalle = "Test", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = fuente.Id, RubroGastoId = rubro.Id,
            CondicionPago = CondicionPago.Contado,
        };
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();
        return gasto.Id;
    }

    [Fact]
    public async Task AgregarAsync_GuardaMetadatosYContenidoPorSeparado()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

        var adjunto = new Adjunto
        {
            NombreArchivo = "factura.pdf", ContentType = "application/pdf",
            TamanoBytes = bytes.Length, GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, bytes);

        var recuperado = await _repo.ObtenerPorIdAsync(id);
        Assert.NotNull(recuperado);
        Assert.Equal("factura.pdf", recuperado!.NombreArchivo);

        var contenido = await _repo.ObtenerContenidoAsync(id);
        Assert.Equal(bytes, contenido);
    }

    [Fact]
    public async Task AgregarAsync_EsAtomica_AdjuntoYContenidoQuedanConsistentes()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

        var adjunto = new Adjunto
        {
            NombreArchivo = "factura.pdf", ContentType = "application/pdf",
            TamanoBytes = bytes.Length, GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, bytes);

        var existeAdjunto = await Context.Adjuntos.AnyAsync(a => a.Id == id);
        var existeContenido = await Context.AdjuntosContenido.AnyAsync(c => c.Id == id);
        Assert.True(existeAdjunto, "El Adjunto debe existir tras AgregarAsync.");
        Assert.True(existeContenido, "El AdjuntoContenido debe existir junto al Adjunto (misma transacción).");

        // Segunda llamada, para confirmar que la transacción de la primera no deja
        // el contexto en un estado que rompa operaciones subsiguientes.
        var id2 = await _repo.AgregarAsync(new Adjunto
        {
            NombreArchivo = "b.pdf", ContentType = "application/pdf",
            TamanoBytes = bytes.Length, GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        }, bytes);
        Assert.True(await Context.AdjuntosContenido.AnyAsync(c => c.Id == id2));
    }

    [Fact]
    public async Task ListarPorGastoAsync_SoloDevuelveActivosDelGasto()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };

        var id1 = await _repo.AgregarAsync(new Adjunto
        {
            NombreArchivo = "a.jpg", ContentType = "image/jpeg", TamanoBytes = bytes.Length,
            GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        }, bytes);

        var lista = await _repo.ListarPorGastoAsync(gastoId);

        Assert.Single(lista);
        Assert.Equal(id1, lista[0].Id);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_NoAparaceEnListado()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };

        var id = await _repo.AgregarAsync(new Adjunto
        {
            NombreArchivo = "a.jpg", ContentType = "image/jpeg", TamanoBytes = bytes.Length,
            GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        }, bytes);

        var adjunto = await _repo.ObtenerPorIdAsync(id);
        adjunto!.Activo = false;
        await _repo.ActualizarAsync(adjunto);

        var lista = await _repo.ListarPorGastoAsync(gastoId);
        Assert.Empty(lista);
    }
}
