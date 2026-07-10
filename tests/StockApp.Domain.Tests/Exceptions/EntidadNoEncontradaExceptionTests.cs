using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class EntidadNoEncontradaExceptionTests
{
    [Fact]
    public void Constructor_ConMensaje_ExponeMessage()
    {
        var ex = new EntidadNoEncontradaException("Producto 5 no encontrado.");

        Assert.Equal("Producto 5 no encontrado.", ex.Message);
    }

    [Fact]
    public void EsException_PeroNoEsReglaDeNegocioException()
    {
        var ex = new EntidadNoEncontradaException("x");

        Assert.IsAssignableFrom<Exception>(ex);
        Assert.IsNotType<ReglaDeNegocioException>(ex);
    }
}
