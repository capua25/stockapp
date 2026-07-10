using System.Net;
using System.Text;
using System.Text.Json;
using StockApp.ApiClient;

namespace StockApp.ApiClient.Tests.TestInfra;

public static class TestHttp
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// HttpClient con la MISMA cadena que arma App.axaml.cs (AuthTokenHandler → transporte),
    /// pero con el transporte falso. BaseAddress con "/" final, igual que en producción.
    /// </summary>
    public static HttpClient CrearCliente(FakeHttpHandler fake, ApiSession? session = null)
    {
        var handler = new AuthTokenHandler(session ?? new ApiSession()) { InnerHandler = fake };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
    }

    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonWeb), Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Problema(HttpStatusCode status, string? detail, string? title = "Error.")
        => Json(new { title, detail, status = (int)status }, status);
}
