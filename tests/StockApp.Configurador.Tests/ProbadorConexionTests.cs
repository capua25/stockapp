using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using StockApp.Configurador.Servicios;
using Xunit;

namespace StockApp.Configurador.Tests;

/// <summary>
/// "Probar conexión" distingue TRES casos (spec 2026-08-20): responde y es la API esperada,
/// responde pero es otra cosa, y no responde. Usa HttpListener real en loopback (sin mocks
/// de HttpClient) para que la aserción cubra el parseo real de la respuesta, no una promesa
/// de que el código "debería" funcionar así.
/// </summary>
public class ProbadorConexionTests
{
    private static (HttpListener listener, string url) IniciarListener(string respuestaJson, int statusCode = 200)
    {
        var puerto = ObtenerPuertoLibre();
        var url = $"http://127.0.0.1:{puerto}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                var contexto = await listener.GetContextAsync();
                contexto.Response.StatusCode = statusCode;
                var bytes = Encoding.UTF8.GetBytes(respuestaJson);
                contexto.Response.ContentType = "application/json";
                contexto.Response.OutputStream.Write(bytes, 0, bytes.Length);
                contexto.Response.OutputStream.Close();
            }
            catch (HttpListenerException)
            {
                // listener detenido mientras esperaba: esperado al hacer Dispose en el test.
            }
            catch (ObjectDisposedException)
            {
                // idem.
            }
        });

        return (listener, url);
    }

    private static int ObtenerPuertoLibre()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var puerto = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return puerto;
    }

    [Fact]
    public async Task ProbarAsync_ApiEsperada_DevuelveOk()
    {
        var (listener, url) = IniciarListener("{\"status\":\"ok\",\"service\":\"StockApp.Api\"}");
        try
        {
            var probador = new ProbadorConexion();

            var resultado = await probador.ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.Ok, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task ProbarAsync_RespondeJsonSinStatusOk_DevuelveRespondeOtraCosa()
    {
        var (listener, url) = IniciarListener("{\"mensaje\":\"hola\"}");
        try
        {
            var probador = new ProbadorConexion();

            var resultado = await probador.ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.RespondeOtraCosa, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task ProbarAsync_RespondeConHttpErrorStatus_DevuelveRespondeOtraCosa()
    {
        var (listener, url) = IniciarListener("{\"status\":\"ok\"}", statusCode: 500);
        try
        {
            var probador = new ProbadorConexion();

            var resultado = await probador.ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.RespondeOtraCosa, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task ProbarAsync_RespondeConCuerpoNoJson_DevuelveRespondeOtraCosa()
    {
        var (listener, url) = IniciarListener("<html>no soy la api</html>");
        try
        {
            var probador = new ProbadorConexion();

            var resultado = await probador.ProbarAsync(url);

            Assert.Equal(ResultadoPruebaConexion.RespondeOtraCosa, resultado);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    [Fact]
    public async Task ProbarAsync_SinNadieEscuchandoEnElPuerto_DevuelveNoHayConexion()
    {
        var puerto = ObtenerPuertoLibre(); // liberado inmediatamente: nadie escucha ahí
        var probador = new ProbadorConexion();

        var resultado = await probador.ProbarAsync($"http://127.0.0.1:{puerto}/");

        Assert.Equal(ResultadoPruebaConexion.NoHayConexion, resultado);
    }

    [Fact]
    public async Task ProbarAsync_UrlMalformada_DevuelveNoHayConexion_NoLanza()
    {
        var probador = new ProbadorConexion();

        var resultado = await probador.ProbarAsync("no-es-una-url");

        Assert.Equal(ResultadoPruebaConexion.NoHayConexion, resultado);
    }

    // ── Bug 2026-09-18: DNS, conexión rechazada, TLS inválido y timeout distinguidos ──────────
    // (antes colapsaban todos en NoResponde). Formas de excepción verificadas empíricamente con
    // un repro standalone (dotnet run, ver reporte de la tarea) antes de escribir este código:
    // conexión rechazada y DNS llegan como HttpRequestException con InnerException
    // SocketException; TLS inválido como HttpRequestException con InnerException
    // AuthenticationException; el timeout de HttpClient.Timeout es un TaskCanceledException
    // (NO envuelto en HttpRequestException) con InnerException TimeoutException.

    [Fact]
    public async Task ProbarAsync_HostNoResuelve_DevuelveNoResuelveNombre()
    {
        var probador = new ProbadorConexion();

        // *.invalid está reservado por RFC 6761 para nunca resolver: no depende de red real.
        var resultado = await probador.ProbarAsync("http://este-host-no-existe-stockapp-repro.invalid/");

        Assert.Equal(ResultadoPruebaConexion.NoResuelveNombre, resultado);
    }

    [Fact]
    public async Task ProbarAsync_CertificadoAutofirmadoNoConfiable_DevuelveCertificadoInvalido()
    {
        var puerto = ObtenerPuertoLibre();
        using var listener = IniciarListenerTlsConCertificadoAutofirmado(puerto);

        var probador = new ProbadorConexion();

        var resultado = await probador.ProbarAsync($"https://127.0.0.1:{puerto}/");

        Assert.Equal(ResultadoPruebaConexion.CertificadoInvalido, resultado);
    }

    [Fact]
    public async Task ProbarAsync_ConectaPeroNuncaResponde_DevuelveTimeout()
    {
        var puerto = ObtenerPuertoLibre();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, puerto);
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                using var cliente = await listener.AcceptTcpClientAsync();
                // Nunca escribe nada: el cliente HTTP se queda esperando la respuesta hasta
                // que vence el timeout corto de este test.
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // listener detenido al hacer Dispose en el test: esperado.
            }
        });

        var probador = new ProbadorConexion(timeoutOverride: TimeSpan.FromMilliseconds(300));

        var resultado = await probador.ProbarAsync($"http://127.0.0.1:{puerto}/");

        Assert.Equal(ResultadoPruebaConexion.Timeout, resultado);
    }

    [Fact]
    public async Task ProbarAsync_CancelacionExternaDelLlamador_PropagaLaCancelacion_NoDevuelveTimeout()
    {
        // Distingue nuestro timeout interno (HttpClient.Timeout -> TimeoutException adentro) de
        // una cancelación real pedida por el llamador vía su propio CancellationToken: esa NO
        // es un resultado nuestro para mapear, tiene que propagarse.
        var puerto = ObtenerPuertoLibre();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, puerto);
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                using var cliente = await listener.AcceptTcpClientAsync();
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // esperado al Dispose del listener.
            }
        });

        var probador = new ProbadorConexion(timeoutOverride: TimeSpan.FromSeconds(30));
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probador.ProbarAsync($"http://127.0.0.1:{puerto}/", cts.Token));
    }

    private static System.Net.Sockets.TcpListener IniciarListenerTlsConCertificadoAutofirmado(int puerto)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certExportable = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), password: null);

        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, puerto);
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                using var cliente = await listener.AcceptTcpClientAsync();
                using var ssl = new System.Net.Security.SslStream(cliente.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(certExportable, clientCertificateRequired: false,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.None,
                    checkCertificateRevocation: false);
            }
            catch
            {
                // el cliente aborta el handshake apenas rechaza el certificado: esperado.
            }
        });

        return listener;
    }
}
