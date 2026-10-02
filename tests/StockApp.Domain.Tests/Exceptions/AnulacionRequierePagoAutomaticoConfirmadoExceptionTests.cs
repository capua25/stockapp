using System.Globalization;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class AnulacionRequierePagoAutomaticoConfirmadoExceptionTests
{
    /// <summary>El mensaje llega tal cual al operador (la Api corre con InvariantGlobalization):
    /// el monto va en es-UY, no como el decimal crudo "1500.5000".</summary>
    [Fact]
    public void Mensaje_MontoEnEsUy()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var ex = new AnulacionRequierePagoAutomaticoConfirmadoException(gastoId: 4, montoPagoAutomatico: 1500.5000m);

            Assert.Contains("pago automático de contado activo por $ 1.500,50:", ex.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
