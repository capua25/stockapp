// tests/StockApp.ApiClient.Tests/ApiErroresTests.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ApiErroresTests
{
    /// <summary>problem+json como lo emite DomainExceptionHandler/JwtBearerEvents de la API.</summary>
    private static HttpResponseMessage Problema(HttpStatusCode status, string? detail, string? title = "Error.")
        => new(status)
        {
            Content = JsonContent.Create(new { title, detail, status = (int)status }),
        };

    [Fact]
    public async Task StatusExitoso_NoLanza()
    {
        await ApiErrores.AsegurarExitoAsync(new HttpResponseMessage(HttpStatusCode.OK));
        await ApiErrores.AsegurarExitoAsync(new HttpResponseMessage(HttpStatusCode.Created));
    }

    [Fact]
    public async Task NotFound_LanzaEntidadNoEncontradaConElDetail()
    {
        var response = Problema(HttpStatusCode.NotFound, "Producto 5 no encontrado.");

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("Producto 5 no encontrado.", ex.Message);
    }

    [Fact]
    public async Task Conflict_LanzaReglaDeNegocioConElDetail()
    {
        var response = Problema(HttpStatusCode.Conflict, "Ya existe una categoría con el nombre 'Bebidas'.");

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("Ya existe una categoría con el nombre 'Bebidas'.", ex.Message);
    }

    [Fact]
    public async Task Conflict_ConExtensionesDeStock_ReconstruyeStockInsuficiente()
    {
        // Mina 2: las extensiones estructuradas del problem+json (Task 5, API) permiten
        // reconstruir la excepción tipada que MovimientoRegistroViewModelBase necesita
        // para el flujo "¿forzar salida?" (usa ex.StockResultante).
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Regla de negocio violada.",
                detail = "Stock insuficiente para el producto 7.",
                status = 409,
                productoId = 7,
                stockActual = 5.0,
                cantidadSolicitada = 8.0,
            }),
        };

        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal(7, ex.ProductoId);
        Assert.Equal(5m, ex.StockActual);
        Assert.Equal(8m, ex.CantidadSolicitada);
        Assert.Equal(-3m, ex.StockResultante);
    }

    [Fact]
    public async Task BadRequest_LanzaArgumentExceptionConElDetail()
    {
        var response = Problema(HttpStatusCode.BadRequest, "La cantidad debe ser mayor que cero.");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("La cantidad debe ser mayor que cero.", ex.Message);
    }

    [Fact]
    public async Task Forbidden_LanzaUnauthorizedAccessConElDetail()
    {
        var response = Problema(HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción.");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("El rol autenticado no tiene permiso para esta acción.", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_LanzaUnauthorizedAccess()
    {
        // El evento de sesión vencida lo dispara AuthTokenHandler (Task 4); acá solo se
        // garantiza que la llamada en curso corta con una excepción que los VMs ya manejan.
        var response = Problema(HttpStatusCode.Unauthorized, "El token es inválido, venció o no fue provisto.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ApiErrores.AsegurarExitoAsync(response));
    }

    [Fact]
    public async Task TooManyRequests_LanzaReglaDeNegocioConElDetail()
    {
        var response = Problema(HttpStatusCode.TooManyRequests, "Demasiados intentos fallidos. Esperá 60 segundos.");

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("Demasiados intentos fallidos. Esperá 60 segundos.", ex.Message);
    }

    [Fact]
    public async Task TooManyRequests_SinDetailNiTitle_UsaMensajeGenericoDeReintento()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("<html>proxy error</html>"),
        };

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("Demasiados intentos, esperá un minuto y volvé a probar.", ex.Message);
    }

    [Fact]
    public async Task Error500_SinDetail_LanzaInvalidOperationConTitleYStatus()
    {
        // La API nunca expone detail en 500 (fail-closed).
        var response = Problema(HttpStatusCode.InternalServerError, detail: null, title: "Error interno.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Contains("500", ex.Message);
        Assert.Contains("Error interno.", ex.Message);
    }

    [Fact]
    public async Task BodyNoJson_UsaElMensajeGenericoConElStatus()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<html>proxy error</html>"),
        };

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("El servidor respondió 404.", ex.Message);
    }

    [Fact]
    public async Task EnviarAsync_HttpRequestException_LanzaServidorNoDisponible()
    {
        var causa = new HttpRequestException("connection refused");

        var ex = await Assert.ThrowsAsync<ServidorNoDisponibleException>(
            () => ApiErrores.EnviarAsync(() => throw causa));

        Assert.Same(causa, ex.InnerException);
    }

    [Fact]
    public async Task EnviarAsync_TaskCanceled_LanzaServidorNoDisponible()
    {
        // HttpClient.Timeout vencido llega como TaskCanceledException. Los clients no pasan
        // CancellationToken propio, así que toda cancelación acá es timeout.
        await Assert.ThrowsAsync<ServidorNoDisponibleException>(
            () => ApiErrores.EnviarAsync(() => throw new TaskCanceledException("timeout")));
    }

    [Fact]
    public async Task EnviarAsync_Exito_DevuelveLaResponse()
    {
        var esperada = new HttpResponseMessage(HttpStatusCode.OK);

        var response = await ApiErrores.EnviarAsync(() => Task.FromResult(esperada));

        Assert.Same(esperada, response);
    }
}
