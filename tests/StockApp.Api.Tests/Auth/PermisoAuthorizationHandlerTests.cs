using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using StockApp.Api.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class PermisoAuthorizationHandlerTests
{
    private sealed class ProveedorPermisosFake : IProveedorPermisos
    {
        public HashSet<string> Permisos { get; set; } = new();
        public Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId) => Task.FromResult<IReadOnlySet<string>>(Permisos);
        public Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos) => Task.CompletedTask;
    }

    private static ClaimsPrincipal Usuario(int id, string rol) => new(new ClaimsIdentity(
        new[]
        {
            new Claim(StockAppClaimTypes.UsuarioId, id.ToString()),
            new Claim(StockAppClaimTypes.Rol, rol),
        },
        authenticationType: "Test"));

    private static async Task<bool> EvaluarAsync(
        ClaimsPrincipal usuario, string permisoRequerido, ProveedorPermisosFake proveedor)
    {
        var handler = new PermisoAuthorizationHandler(proveedor);
        var requirement = new PermisoRequirement(permisoRequerido);
        var context = new AuthorizationHandlerContext(new[] { requirement }, usuario, resource: null);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Admin_PasaCualquierPermiso_SinConsultarElProveedor()
    {
        var proveedor = new ProveedorPermisosFake();

        var exito = await EvaluarAsync(Usuario(1, "Admin"), Permisos.GestionarUsuarios, proveedor);

        Assert.True(exito);
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    public async Task Operador_LosCuatroEstructurales_RechazanAunqueElProveedorLosTuviera(string permisoEstructural)
    {
        // Sesión "envenenada": el proveedor devuelve el permiso estructural, pero el handler
        // tiene que rechazar igual — el corte es ANTES de consultar el proveedor.
        var proveedor = new ProveedorPermisosFake { Permisos = { permisoEstructural } };

        var exito = await EvaluarAsync(Usuario(2, "Operador"), permisoEstructural, proveedor);

        Assert.False(exito);
    }

    [Fact]
    public async Task Operador_ConElPermisoEnElProveedor_Pasa()
    {
        var proveedor = new ProveedorPermisosFake { Permisos = { Permisos.VerFinanzas } };

        var exito = await EvaluarAsync(Usuario(2, "Operador"), Permisos.VerFinanzas, proveedor);

        Assert.True(exito);
    }

    [Fact]
    public async Task Operador_SinElPermisoEnElProveedor_Rechaza()
    {
        var proveedor = new ProveedorPermisosFake { Permisos = { Permisos.GestionarProductos } };

        var exito = await EvaluarAsync(Usuario(2, "Operador"), Permisos.VerFinanzas, proveedor);

        Assert.False(exito);
    }

    [Fact]
    public async Task SinClaimDeRol_Rechaza()
    {
        var sinRol = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(StockAppClaimTypes.UsuarioId, "3") }, authenticationType: "Test"));

        var exito = await EvaluarAsync(sinRol, Permisos.VerFinanzas, new ProveedorPermisosFake());

        Assert.False(exito);
    }
}
