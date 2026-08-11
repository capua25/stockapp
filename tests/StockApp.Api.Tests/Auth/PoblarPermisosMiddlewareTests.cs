using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class PoblarPermisosMiddlewareTests : ApiTestBase
{
    public PoblarPermisosMiddlewareTests(ApiFactory factory) : base(factory) { }

    private async Task<(string Token, int UsuarioId)> CrearOperadorConTokenAsync(string nombre)
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, nombre, "Secreta123!", RolUsuario.Operador);
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(usuario.Id, RolUsuario.Operador);
        return (token, usuario.Id);
    }

    // SeedUsuarioAsync siembra los 9 PermisosInicialesOperador completos (incluye
    // GestionarTareas) — no sirve para probar el 403 por falta de UN permiso puntual.
    // SeedOperadorConPermisosAsync existe justamente para eso: un subconjunto explícito que lo
    // excluye, igual que un Admin que le recortó permisos a un Operador real (Task 10).
    private async Task<(string Token, int UsuarioId)> CrearOperadorSinGestionarTareasConTokenAsync(string nombre)
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, nombre, "Secreta123!", new[] { Permisos.GestionarProductos });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(usuario.Id, RolUsuario.Operador);
        return (token, usuario.Id);
    }

    [Fact]
    public async Task OperadorConElPermisoEnPermisoUsuario_LlegaAlEndpointYObtiene201()
    {
        // POST /tareas exige GestionarTareas (permiso configurable). Si el middleware no
        // poblara ICurrentSession.PermisosActuales antes de que TareaService.AltaAsync llame
        // a AuthorizationService.Verificar, esto tendría que dar 403 sin importar la fila real
        // en PermisoUsuario — el hecho de que dé 201 prueba que el middleware corrió.
        var (token, usuarioId) = await CrearOperadorConTokenAsync("operador.conpermiso");
        // IProveedorPermisos está registrado Scoped en la collection "Api" (ver ApiFactory,
        // commit b324f52) para aislar la cache entre tests — no se puede resolver desde el
        // root provider (Factory.Services) directamente, hace falta un scope propio.
        using (var scope = Factory.Services.CreateScope())
        {
            var proveedor = scope.ServiceProvider.GetRequiredService<IProveedorPermisos>();
            await proveedor.GuardarAsync(usuarioId, new[] { Permisos.GestionarTareas });
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/tareas", new { Titulo = "Tarea de prueba" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OperadorSinElPermisoEnPermisoUsuario_Recibe403()
    {
        var (token, _) = await CrearOperadorSinGestionarTareasConTokenAsync("operador.sinpermiso");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/tareas", new { Titulo = "Tarea de prueba" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RutaAnonima_SigueFuncionandoSinExcepciones()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "no-existe", Contrasena = "no-importa" });

        // 401 esperado (credenciales inválidas) — lo que importa es que el pipeline no reviente
        // con una excepción no controlada al pasar por el middleware nuevo sin usuario autenticado.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
