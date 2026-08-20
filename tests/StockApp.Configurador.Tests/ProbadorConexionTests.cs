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
    public async Task ProbarAsync_SinNadieEscuchandoEnElPuerto_DevuelveNoResponde()
    {
        var puerto = ObtenerPuertoLibre(); // liberado inmediatamente: nadie escucha ahí
        var probador = new ProbadorConexion();

        var resultado = await probador.ProbarAsync($"http://127.0.0.1:{puerto}/");

        Assert.Equal(ResultadoPruebaConexion.NoResponde, resultado);
    }

    [Fact]
    public async Task ProbarAsync_UrlMalformada_DevuelveNoResponde_NoLanza()
    {
        var probador = new ProbadorConexion();

        var resultado = await probador.ProbarAsync("no-es-una-url");

        Assert.Equal(ResultadoPruebaConexion.NoResponde, resultado);
    }
}
