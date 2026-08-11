using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
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
    [InlineData(Permisos.AdministrarTareas)]
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
    [InlineData(Permisos.AdministrarTareas)]
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
            () => _svc.Verificar((RolUsuario?)null, Permisos.GestionarProductos));
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
    [InlineData(Permisos.AdministrarTareas)]
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
    [InlineData(Permisos.AdministrarTareas)]
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

    // ── Verificar(ICurrentSession, string) — nuevo overload (spec 2026-08-10) ──────────────

    private sealed class SesionFake : ICurrentSession
    {
        public bool EstaAutenticado { get; set; } = true;
        public UsuarioSesion? UsuarioActual { get; set; }
        public RolUsuario? RolActual { get; set; }
        public IReadOnlySet<string> PermisosActuales { get; set; } = new HashSet<string>();
        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException();
        public void CerrarSesion() => throw new NotSupportedException();
        public void EstablecerPermisos(IReadOnlySet<string> permisos) => PermisosActuales = permisos;
    }

    [Fact]
    public void VerificarConSesion_SinAutenticar_LanzaUnauthorized()
    {
        var sesion = new SesionFake { EstaAutenticado = false };

        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(sesion, Permisos.VerFinanzas));
    }

    [Fact]
    public void VerificarConSesion_Admin_PasaSiempre_SinConsultarPermisosActuales()
    {
        // PermisosActuales queda deliberadamente null-like (vacío): si Verificar lo consultara
        // para Admin, este test lo detectaría por Assert.True en vez de simplemente no lanzar.
        var sesion = new SesionFake { RolActual = RolUsuario.Admin, PermisosActuales = new HashSet<string>() };

        var ex = Record.Exception(() => _svc.Verificar(sesion, Permisos.GestionarUsuarios));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    public void VerificarConSesion_Operador_LosCuatroEstructurales_RechazanSiempre(string permisoEstructural)
    {
        var sesion = new SesionFake { RolActual = RolUsuario.Operador, PermisosActuales = new HashSet<string>() };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesion, permisoEstructural));
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    public void VerificarConSesion_SesionEnvenenada_LosCuatroEstructuralesRechazanIgual(string permisoEstructural)
    {
        // El test más importante de esta clase: aunque PermisosActuales CONTENGA el permiso
        // estructural (una fila colada por error, o un bug futuro en PUT /usuarios/{id}/permisos),
        // Verificar tiene que rechazar igual. Este es el corte que el spec marca como "el punto
        // de falla más peligroso" — si se invirtiera el orden, un Operador con una fila colada
        // podría auto-otorgarse GestionarUsuarios y desde ahí cualquier otro permiso.
        var sesionEnvenenada = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { permisoEstructural },
        };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesionEnvenenada, permisoEstructural));
    }

    [Fact]
    public void VerificarConSesion_Operador_ConElPermisoEnPermisosActuales_Pasa()
    {
        var sesion = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { Permisos.VerFinanzas },
        };

        var ex = Record.Exception(() => _svc.Verificar(sesion, Permisos.VerFinanzas));

        Assert.Null(ex);
    }

    [Fact]
    public void VerificarConSesion_Operador_SinElPermisoEnPermisosActuales_Lanza()
    {
        var sesion = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { Permisos.GestionarProductos },
        };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesion, Permisos.VerFinanzas));
    }

    [Fact]
    public void PermisosEstructuralesAdmin_ContieneExactamenteLosCuatroDocumentados()
    {
        Assert.Equal(4, AuthorizationService.PermisosEstructuralesAdmin.Count);
        Assert.Contains(Permisos.GestionarUsuarios, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.ImportarPlanillas, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.GestionarDiagnostico, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.AdministrarTareas, AuthorizationService.PermisosEstructuralesAdmin);
    }

    [Fact]
    public void PermisosConfigurables_TieneLos11RestantesYNoIntersecaConLosEstructurales()
    {
        Assert.Equal(11, AuthorizationService.PermisosConfigurables.Count);
        foreach (var permiso in AuthorizationService.PermisosConfigurables)
            Assert.DoesNotContain(permiso, AuthorizationService.PermisosEstructuralesAdmin);
    }

    [Fact]
    public void PermisosInicialesOperador_TieneExactamenteLos9DeAccionesOperadorEnOrden()
    {
        Assert.Equal(new[]
        {
            Permisos.GestionarProductos,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
            Permisos.VerFinanzas,
            Permisos.GestionarMaestrosFinanzas,
            Permisos.RegistrarGastos,
            Permisos.RegistrarPagos,
            Permisos.RegistrarIngresos,
            Permisos.GestionarTareas,
        }, AuthorizationService.PermisosInicialesOperador);
    }
}
