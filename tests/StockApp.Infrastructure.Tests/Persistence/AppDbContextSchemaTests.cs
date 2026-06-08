using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

public class AppDbContextSchemaTests
{
    private static AppDbContext CrearContextoEnMemoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public void AppDbContext_PuedeCrearEsquemaCompleto()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AppDbContext_Usuarios_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.Usuarios);
    }

    [Fact]
    public void AppDbContext_Productos_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.Productos);
    }

    [Fact]
    public void AppDbContext_MovimientosStock_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.MovimientosStock);
    }

    [Fact]
    public void AppDbContext_LogsAuditoria_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.LogsAuditoria);
    }

    [Fact]
    public void AppDbContext_NombreUsuario_TieneIndiceUnico()
    {
        using var ctx = CrearContextoEnMemoria();

        var entityType = ctx.Model.FindEntityType(typeof(StockApp.Domain.Entities.Usuario))!;
        var indices = entityType.GetIndexes();

        Assert.Contains(indices, idx =>
            idx.IsUnique &&
            idx.Properties.Any(p => p.Name == nameof(StockApp.Domain.Entities.Usuario.NombreUsuario)));
    }

    [Fact]
    public void AppDbContext_Codigo_SKU_TieneIndiceUnico()
    {
        using var ctx = CrearContextoEnMemoria();

        var entityType = ctx.Model.FindEntityType(typeof(StockApp.Domain.Entities.Producto))!;
        var indices = entityType.GetIndexes();

        Assert.Contains(indices, idx =>
            idx.IsUnique &&
            idx.Properties.Any(p => p.Name == nameof(StockApp.Domain.Entities.Producto.Codigo)));
    }

    [Fact]
    public void AppDbContext_PuedeInsertar_Y_Recuperar_UnProducto()
    {
        using var ctx = CrearContextoEnMemoria();

        var unidad = new StockApp.Domain.Entities.UnidadMedida
            { Nombre = "Unidad", Abreviatura = "u" };
        ctx.UnidadesMedida.Add(unidad);
        ctx.SaveChanges();

        var producto = new StockApp.Domain.Entities.Producto
        {
            Codigo = "SKU-001",
            Nombre = "Tornillo 6x1",
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 5.00m,
            PrecioVenta = 8.50m,
            FechaAlta = DateTime.UtcNow
        };
        ctx.Productos.Add(producto);
        ctx.SaveChanges();

        var recuperado = ctx.Productos.First(p => p.Codigo == "SKU-001");
        Assert.Equal("Tornillo 6x1", recuperado.Nombre);
        Assert.Equal(8.50m, recuperado.PrecioVenta);
    }

    [Fact]
    public void AppDbContext_NombreUsuario_UnicoRechazaDuplicados()
    {
        using var ctx = CrearContextoEnMemoria();

        ctx.Usuarios.Add(new StockApp.Domain.Entities.Usuario
        {
            NombreUsuario = "admin",
            HashContrasena = "hash1",
            Rol = StockApp.Domain.Enums.RolUsuario.Admin,
            FechaAlta = DateTime.UtcNow
        });
        ctx.SaveChanges();

        ctx.Usuarios.Add(new StockApp.Domain.Entities.Usuario
        {
            NombreUsuario = "admin", // duplicado
            HashContrasena = "hash2",
            Rol = StockApp.Domain.Enums.RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow
        });

        Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void AppDbContext_PrecisionDecimal_NoTruncaPrecios()
    {
        using var ctx = CrearContextoEnMemoria();

        var unidad = new StockApp.Domain.Entities.UnidadMedida
            { Nombre = "Litro", Abreviatura = "l" };
        ctx.UnidadesMedida.Add(unidad);
        ctx.SaveChanges();

        var producto = new StockApp.Domain.Entities.Producto
        {
            Codigo = "SKU-DEC",
            Nombre = "Pintura",
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 1234.5678m,
            PrecioVenta = 9876.1234m,
            FechaAlta = DateTime.UtcNow
        };
        ctx.Productos.Add(producto);
        ctx.SaveChanges();

        ctx.ChangeTracker.Clear();
        var leido = ctx.Productos.Single(p => p.Codigo == "SKU-DEC");
        Assert.Equal(1234.5678m, leido.PrecioCosto);
        Assert.Equal(9876.1234m, leido.PrecioVenta);
    }
}
