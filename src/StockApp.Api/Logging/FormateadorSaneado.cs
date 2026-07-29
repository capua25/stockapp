using Serilog.Events;
using Serilog.Formatting;

namespace StockApp.Api.Logging;

/// <summary>
/// Envuelve a otro <see cref="ITextFormatter"/>: lo deja renderizar el evento completo
/// (mensaje + excepcion) a un buffer en memoria, sanea ese texto y recien ahi lo escribe
/// a la salida real. Es el unico punto por el que pasa TODO lo que va a terminar en el
/// archivo de log, incluido el stack trace, que un enricher no puede tocar.
/// </summary>
internal sealed class FormateadorSaneado : ITextFormatter
{
    private readonly ITextFormatter _interno;

    internal FormateadorSaneado(ITextFormatter interno) => _interno = interno;

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var buffer = new StringWriter();
        _interno.Format(logEvent, buffer);
        output.Write(SaneadorCredenciales.Sanear(buffer.ToString()));
    }
}
