using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Test de concurrencia MANDATORY (design §5): dos salidas simultáneas sobre el mismo
/// producto con stock limitado. Exactamente una tiene éxito; el stock nunca queda negativo;
/// no hay lost-update. Cada tarea usa su PROPIO AppDbContext (un DbContext no es thread-safe).
/// Requiere Docker (Testcontainers PostgreSQL).
/// </summary>
public class MovimientoStockConcurrenciaTests : PostgresRepositoryTestBase
{
    public MovimientoStockConcurrenciaTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DosSalidasSimultaneas_MismoProducto_UnaSolaTieneExito()
    {
        // Seed: stock=10, cada salida pide 8 → solo una puede pasar
        var um = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" };
        var usuario = new Usuario
        {
            NombreUsuario = "conc_user", HashContrasena = "hash",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = "CONC001", Nombre = "Producto Concurrente", UnidadMedida = um,
            PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 10m, Activo = true, FechaAlta = DateTime.UtcNow
        };
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();
        int productoId = producto.Id;
        int usuarioId = usuario.Id;

        // Cada tarea con su propio contexto y repo (aislamiento de conexión)
        async Task<ResultadoRegistro> SalidaAsync()
        {
            await using var ctx = Fixture.CrearContexto();
            var repo = new MovimientoStockRepository(ctx);
            var mov = new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = usuarioId, Tipo = TipoMovimiento.Salida,
                Cantidad = 8m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta
            };
            var args = new RegistroAtomicoArgs(
                Movimiento: mov, ProductoId: productoId, Tipo: TipoMovimiento.Salida,
                Cantidad: 8m, Forzar: false, UsuarioId: usuarioId, DetalleAuditoria: "salida concurrente");
            return await repo.RegistrarMovimientoAtomicoAsync(args);
        }

        var resultados = await Task.WhenAll(SalidaAsync(), SalidaAsync());

        // Exactamente una Ok y una StockInsuficiente
        Assert.Equal(1, resultados.Count(r => r.Estado == ResultadoRegistroEstado.Ok));
        Assert.Equal(1, resultados.Count(r => r.Estado == ResultadoRegistroEstado.StockInsuficiente));

        await using var verify = Fixture.CrearContexto();
        var stockFinal = await verify.Productos
            .Where(p => p.Id == productoId).Select(p => p.StockActual).FirstAsync();
        Assert.Equal(2m, stockFinal);                       // 10 - 8, nunca negativo, sin lost-update
        Assert.Equal(1, await verify.MovimientosStock.CountAsync());   // solo la salida ganadora
        Assert.Equal(1, await verify.LogsAuditoria.CountAsync());
    }
}
