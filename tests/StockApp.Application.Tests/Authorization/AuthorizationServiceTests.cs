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
    [InlineData(Permisos.GestionarProductos)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.GestionarTablasMaestras)]
    [InlineData(Permisos.ImportarPlanillas)]
    public void Admin_PuedeEjecutarCualquierAccion(string accion)
    {
        // No debe lanzar
        _svc.Verificar(RolUsuario.Admin, accion);
    }

    // ── Operador: acciones permitidas ────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarProductos)]
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
    [InlineData(Permisos.ImportarPlanillas)]
    public void Operador_NoPuedeEjecutarAccionesDeAdmin(string accion)
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(RolUsuario.Operador, accion));
    }

    // ── Operador NO puede gestionar tablas maestras ──────────────────────────

    [Fact]
    public void Operador_NoTieneGestionarTablasMaestras_LanzaUnauthorized()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(RolUsuario.Operador, Permisos.GestionarTablasMaestras));
    }

    // ── Sin sesión ────────────────────────────────────────────────────────────

    [Fact]
    public void SinSesion_CualquierAccionLanzaExcepcion()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(null, Permisos.GestionarProductos));
    }

    // ── TienePermiso (Fase 2b, D1): consulta sin lanzar, misma tabla que Verificar ──

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    [InlineData(Permisos.GestionarProductos)]
    [InlineData(Permisos.GestionarTablasMaestras)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.RecalcularStock)]
    [InlineData(Permisos.ImportarPlanillas)]
    public void TienePermiso_Admin_DevuelveTrueParaTodo(string accion)
    {
        Assert.True(_svc.TienePermiso(RolUsuario.Admin, accion));
    }

    [Theory]
    [InlineData(Permisos.GestionarProductos)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.RecalcularStock)]
    public void TienePermiso_Operador_DevuelveTrueParaAccionesOperativas(string accion)
    {
        Assert.True(_svc.TienePermiso(RolUsuario.Operador, accion));
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    [InlineData(Permisos.GestionarTablasMaestras)]
    [InlineData(Permisos.ImportarPlanillas)]
    public void TienePermiso_Operador_DevuelveFalseParaAccionesDeAdmin(string accion)
    {
        Assert.False(_svc.TienePermiso(RolUsuario.Operador, accion));
    }

    [Fact]
    public void TienePermiso_NuncaLanza_ADiferenciaDeVerificar()
    {
        var ex = Record.Exception(() => _svc.TienePermiso(RolUsuario.Operador, Permisos.GestionarUsuarios));
        Assert.Null(ex);
    }
}
