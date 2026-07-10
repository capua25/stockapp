namespace StockApp.ApiClient.Tests.TestInfra;

/// <summary>
/// HttpMessageHandler falso: captura la última request (método, URI, body serializado)
/// y responde lo que indique el responder. Doble de test de TODOS los XxxApiClientTests.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? UltimaRequest { get; private set; }
    public string? UltimoBody { get; private set; }

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        UltimaRequest = request;
        UltimoBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responder(request);
    }
}
