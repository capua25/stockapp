using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Services;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

/// <summary>
/// AuditService contra PostgreSQL real (Testcontainers). Cada test parte de tablas
/// truncadas (PostgresRepositoryTestBase). Requiere Docker.
/// </summary>
public class AuditServiceTests : PostgresRepositoryTestBase
{
    private readonly AuditService _svc;
    private readonly int _usuarioId;

    public AuditServiceTests(PostgresFixture fixture) : base(fixture)
    {
        // Usuario de referencia para la FK
        var usuario = new Usuario
        {
            NombreUsuario = "admin", HashContrasena = "h",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        Context.Usuarios.Add(usuario);
        Context.SaveChanges();
        _usuarioId = usuario.Id;

        _svc = new AuditService(Context);
    }

    [Fact]
    public async Task RegistrarAsync_AltaUsuario_InsertaLogEnBd()
    {
        await _svc.RegistrarAsync(_usuarioId, AccionAuditada.AltaUsuario, "Usuario", 99, "Alta de 'x'");

        var log = await Context.LogsAuditoria.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(AccionAuditada.AltaUsuario, log!.Accion);
        Assert.Equal("Usuario", log.Entidad);
        Assert.Equal(99, log.EntidadId);
    }

    [Fact]
    public async Task RegistrarAsync_BajaUsuario_InsertaLogConDetalle()
    {
        await _svc.RegistrarAsync(_usuarioId, AccionAuditada.BajaUsuario, "Usuario", 5, "Baja lógica de 'jperez'");

        var log = await Context.LogsAuditoria
            .FirstOrDefaultAsync(l => l.Accion == AccionAuditada.BajaUsuario);
        Assert.NotNull(log);
        Assert.Contains("jperez", log!.Detalle);
    }

    [Fact]
    public async Task RegistrarAsync_CambioRol_GuardaFechaActual()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);
        await _svc.RegistrarAsync(_usuarioId, AccionAuditada.CambioRol, "Usuario", 2, "Rol: Operador → Admin");

        var log = await Context.LogsAuditoria
            .FirstOrDefaultAsync(l => l.Accion == AccionAuditada.CambioRol);
        Assert.NotNull(log);
        Assert.True(log!.Fecha >= antes);
    }
}
