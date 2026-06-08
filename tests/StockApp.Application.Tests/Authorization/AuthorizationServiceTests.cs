using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class AuthorizationServiceTests
{
    private readonly AuthorizationService _svc = new();

    // ── Admin puede todo ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    [InlineData(Permisos.GestionarCatalogo)]
    [InlineData(Permisos.RegistrarMovimientos)]
    public void Admin_PuedeEjecutarCualquierAccion(string accion)
    {
        // No debe lanzar
        _svc.Verificar(RolUsuario.Admin, accion);
    }

    // ── Operador: acciones permitidas ────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarCatalogo)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.RecalcularStock)]
    public void Operador_PuedeEjecutarAccionesOperativas(string accion)
    {
        // No debe lanzar
        _svc.Verificar(RolUsuario.Operador, accion);
    }

    // ── Operador: acciones denegadas ─────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    public void Operador_NoPuedeEjecutarAccionesDeAdmin(string accion)
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(RolUsuario.Operador, accion));
    }

    // ── Sin sesión ────────────────────────────────────────────────────────────

    [Fact]
    public void SinSesion_CualquierAccionLanzaExcepcion()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(null, Permisos.GestionarCatalogo));
    }
}
