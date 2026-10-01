using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class StockInsuficienteExceptionTests
{
    [Fact]
    public void StockInsuficienteException_Propiedades_SonCorrectas()
    {
        var ex = new StockInsuficienteException(productoId: 5, stockActual: 3, cantidadSolicitada: 10);

        Assert.Equal(5, ex.ProductoId);
        Assert.Equal(3, ex.StockActual);
        Assert.Equal(10, ex.CantidadSolicitada);
        Assert.Equal(-7, ex.StockResultante);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void EsReglaDeNegocioException()
    {
        var ex = new StockInsuficienteException(productoId: 5, stockActual: 3, cantidadSolicitada: 10);

        Assert.IsAssignableFrom<ReglaDeNegocioException>(ex);
    }

    [Fact]
    public void Mensaje_CantidadesEnEsUy_SinEscalaInterna()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            var ex = new StockInsuficienteException(productoId: 5, stockActual: 1500.5000m, cantidadSolicitada: 2000.25m);

            Assert.Contains("tenés 1.500,5 unidades pero solicitaste 2.000,25", ex.Message);
            Assert.Contains("El stock resultante sería -499,75", ex.Message);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
