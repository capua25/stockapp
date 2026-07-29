using System.Globalization;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;
using StockApp.Api.Logging;

namespace StockApp.Api.Tests.Logging;

public class FormateadorSaneadoTests
{
    private static readonly MessageTemplateParser Parser = new();

    private static FormateadorSaneado Crear() =>
        new(new MessageTemplateTextFormatter("{Message:lj}{NewLine}{Exception}", CultureInfo.InvariantCulture));

    private static LogEvent CrearEvento(string plantilla, Exception? excepcion = null) =>
        new(DateTimeOffset.UnixEpoch, LogEventLevel.Warning, excepcion,
            Parser.Parse(plantilla), []);

    [Fact]
    public void Format_ConCredencialEnElMensaje_LaEnmascara()
    {
        var formateador = Crear();
        var evento = CrearEvento("No se pudo conectar: Password=secreta-del-municipio;");
        var salida = new StringWriter();

        formateador.Format(evento, salida);

        var texto = salida.ToString();
        Assert.DoesNotContain("secreta-del-municipio", texto);
        Assert.Contains("Password=***", texto);
    }

    [Fact]
    public void Format_ConCredencialEnElStackTrace_LaEnmascara()
    {
        var formateador = Crear();
        var excepcion = new InvalidOperationException(
            "Npgsql fallo con Host=localhost;Password=secreta-en-excepcion;");
        var evento = CrearEvento("Error al abrir la conexion", excepcion);
        var salida = new StringWriter();

        formateador.Format(evento, salida);

        var texto = salida.ToString();
        Assert.DoesNotContain("secreta-en-excepcion", texto);
        Assert.Contains("Password=***", texto);
    }

    [Fact]
    public void Format_SinCredenciales_DejaElMensajeIntacto()
    {
        var formateador = Crear();
        var evento = CrearEvento("La corrida de backup fallo por timeout");
        var salida = new StringWriter();

        formateador.Format(evento, salida);

        Assert.Contains("La corrida de backup fallo por timeout", salida.ToString());
    }
}
