using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.ErrorHandling;
using StockApp.Domain.Exceptions;
using System.Text.Json;
using Xunit;

namespace StockApp.Api.Tests.ErrorHandling;

/// <summary>
/// Test unitario del handler, sin WebApplicationFactory: arma un DefaultHttpContext
/// con un IProblemDetailsService real (via ServiceCollection mínimo) e invoca
/// TryHandleAsync directamente. Mismo espíritu que JwtTokenServiceTests (Fase 2a,
/// Task 2): no hace falta un host HTTP completo para probar una unidad aislada.
/// </summary>
public class DomainExceptionHandlerTests
{
    private static async Task<(int Status, string ContentType, JsonDocument Body)> EjecutarAsync(Exception excepcion)
    {
        var services = new ServiceCollection();
        services.AddProblemDetails();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            Response = { Body = new MemoryStream() },
        };

        var handler = new DomainExceptionHandler();
        var manejada = await handler.TryHandleAsync(context, excepcion, CancellationToken.None);

        Assert.True(manejada);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var texto = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, context.Response.ContentType!, JsonDocument.Parse(texto));
    }

    [Fact]
    public async Task StockInsuficienteException_Mapea409()
    {
        var (status, contentType, body) = await EjecutarAsync(
            new StockInsuficienteException(1, 5m, 10m));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.StartsWith("application/problem+json", contentType);
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InvalidOperationException_Generica_Mapea500SinExponerElMensajeInterno()
    {
        // Fase 3a, D4: ningún servicio de Application lanza esta excepción genérica del BCL —
        // solo las de dominio propias (ReglaDeNegocioException/EntidadNoEncontradaException).
        // Si algo la lanza igual (código nuevo que no siguió la convención), es un error no
        // anticipado: cae al 500 fail-closed, no a un 409 que sugeriría una regla de negocio real.
        var (status, _, body) = await EjecutarAsync(new InvalidOperationException("detalle interno"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        var tieneDetail = body.RootElement.TryGetProperty("detail", out var detalle);
        if (tieneDetail)
            Assert.DoesNotContain("detalle interno", detalle.GetString());
    }

    [Fact]
    public async Task KeyNotFoundException_Generica_Mapea500SinExponerElMensajeInterno()
    {
        var (status, _, body) = await EjecutarAsync(new KeyNotFoundException("detalle interno"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        var tieneDetail = body.RootElement.TryGetProperty("detail", out var detalle);
        if (tieneDetail)
            Assert.DoesNotContain("detalle interno", detalle.GetString());
    }

    [Fact]
    public async Task ArgumentException_Mapea400()
    {
        var (status, _, _) = await EjecutarAsync(new ArgumentException("dato invalido"));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task UnauthorizedAccessException_Mapea403()
    {
        var (status, _, _) = await EjecutarAsync(new UnauthorizedAccessException("sin permiso"));
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task BadHttpRequestException_Mapea400ConStatusCodeDeLaExcepcion()
    {
        // Binding fallido de Minimal API (ej. enum inválido en query string) tira esta
        // excepción con StatusCode 400 propio; el handler debe respetarlo, no caer al 500 genérico.
        var (status, _, body) = await EjecutarAsync(new BadHttpRequestException("mensaje de binding"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("mensaje de binding", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExcepcionGenerica_Mapea500SinExponerElMensajeInterno()
    {
        var (status, _, body) = await EjecutarAsync(new Exception("detalle interno sensible"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        var tieneDetail = body.RootElement.TryGetProperty("detail", out var detalle);
        if (tieneDetail)
            Assert.DoesNotContain("detalle interno sensible", detalle.GetString());
    }

    [Fact]
    public async Task EntidadNoEncontradaException_Mapea404()
    {
        var (status, _, _) = await EjecutarAsync(new EntidadNoEncontradaException("Producto 5 no encontrado."));
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task ReglaDeNegocioException_Mapea409()
    {
        var (status, _, _) = await EjecutarAsync(new ReglaDeNegocioException("Ya existe una categoría con ese nombre."));
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task StockInsuficienteException_IncluyeLosDatosEstructuradosComoExtensiones()
    {
        // Fase 3b (Mina 2): el cliente HTTP del desktop reconstruye StockInsuficienteException
        // desde estas extensiones para preservar el flujo "¿forzar salida?" del ViewModel
        // (que usa ex.StockResultante). Sin ellas, el 409 solo permite un ReglaDeNegocio plano.
        var (status, _, body) = await EjecutarAsync(new StockInsuficienteException(7, 5m, 8m));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal(7, body.RootElement.GetProperty("productoId").GetInt32());
        Assert.Equal(5m, body.RootElement.GetProperty("stockActual").GetDecimal());
        Assert.Equal(8m, body.RootElement.GetProperty("cantidadSolicitada").GetDecimal());
    }

    [Fact]
    public async Task ReglaDeNegocioException_NoIncluyeLasExtensionesDeStock()
    {
        var (_, _, body) = await EjecutarAsync(new ReglaDeNegocioException("Ya existe."));

        Assert.False(body.RootElement.TryGetProperty("productoId", out _));
        Assert.False(body.RootElement.TryGetProperty("stockActual", out _));
    }
}
