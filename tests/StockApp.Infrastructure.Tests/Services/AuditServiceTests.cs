using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly AuditService _svc;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();

        // Usuario de referencia para la FK
        _ctx.Usuarios.Add(new Usuario
        {
            Id = 1, NombreUsuario = "admin", HashContrasena = "h",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        });
        _ctx.SaveChanges();

        _svc = new AuditService(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task RegistrarAsync_AltaUsuario_InsertaLogEnBd()
    {
        await _svc.RegistrarAsync(1, AccionAuditada.AltaUsuario, "Usuario", 99, "Alta de 'x'");

        var log = await _ctx.LogsAuditoria.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(AccionAuditada.AltaUsuario, log!.Accion);
        Assert.Equal("Usuario", log.Entidad);
        Assert.Equal(99, log.EntidadId);
    }

    [Fact]
    public async Task RegistrarAsync_BajaUsuario_InsertaLogConDetalle()
    {
        await _svc.RegistrarAsync(1, AccionAuditada.BajaUsuario, "Usuario", 5, "Baja lógica de 'jperez'");

        var log = await _ctx.LogsAuditoria
            .FirstOrDefaultAsync(l => l.Accion == AccionAuditada.BajaUsuario);
        Assert.NotNull(log);
        Assert.Contains("jperez", log!.Detalle);
    }

    [Fact]
    public async Task RegistrarAsync_CambioRol_GuardaFechaActual()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);
        await _svc.RegistrarAsync(1, AccionAuditada.CambioRol, "Usuario", 2, "Rol: Operador → Admin");

        var log = await _ctx.LogsAuditoria
            .FirstOrDefaultAsync(l => l.Accion == AccionAuditada.CambioRol);
        Assert.NotNull(log);
        Assert.True(log!.Fecha >= antes);
    }
}
