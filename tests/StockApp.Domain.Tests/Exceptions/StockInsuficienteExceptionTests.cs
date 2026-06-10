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
}
