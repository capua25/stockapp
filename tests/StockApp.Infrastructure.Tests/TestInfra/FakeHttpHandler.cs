namespace StockApp.Infrastructure.Tests.TestInfra;

public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? UltimaRequest { get; private set; }
    public string? UltimoBody { get; private set; }
    public int Llamadas { get; private set; }

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Llamadas++;
        UltimaRequest = request;
        UltimoBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responder(request);
    }
}
