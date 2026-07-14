using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Reportes;

/// <summary>
/// Test de integración de oro del caché de reportes (Task 4): prueba el flujo completo
/// end-to-end (registro del decorator en Program.cs + invalidación vía IVersionReportes)
/// sin mockear nada — si la invalidación no funcionara, la segunda lectura devolvería el
/// total stale de la primera.
/// </summary>
public class CacheReportesIntegracionTests : ApiTestBase
{
    public CacheReportesIntegracionTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    [Fact]
    public async Task Valorizacion_TrasUnMovimiento_ReflejaElCambio()
    {
        // 1. Admin sembrado (ocupa Id=1, coincide con TokenAdmin() — el registro del
        //    movimiento escribe auditoría con ese UsuarioId, que requiere FK a un Usuario
        //    real) + producto con stock y precios conocidos (PrecioCosto=10m, StockActual=5m).
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-CACHE-1", "Producto Cache 1");

        // 2. Token admin (permiso VerReportes + RegistrarMovimientos).
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        // 3. Valorización inicial: dispara el cálculo y lo deja en caché.
        var inicial = await client.GetFromJsonAsync<ValorizacionReporteDto>("/reportes/valorizacion");
        var totalInicial = inicial!.Totales.TotalValorCosto;

        // 4. Movimiento de entrada que cambia el stock del producto sembrado.
        var nuevoMovimiento = new RegistrarMovimientoRequest(
            producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra,
            Cantidad: 20m, PrecioUnitario: 10m, Comentario: null);

        var resp = await client.PostAsJsonAsync("/movimientos", nuevoMovimiento);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // 5. Valorización tras el movimiento: si la invalidación no funcionara, acá se
        //    devolvería el total stale (idéntico al inicial) en vez del actualizado.
        var despues = await client.GetFromJsonAsync<ValorizacionReporteDto>("/reportes/valorizacion");
        Assert.NotEqual(totalInicial, despues!.Totales.TotalValorCosto);
    }
}
