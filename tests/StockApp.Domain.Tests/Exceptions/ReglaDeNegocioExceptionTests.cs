using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class ReglaDeNegocioExceptionTests
{
    [Fact]
    public void Constructor_ConMensaje_ExponeMessage()
    {
        var ex = new ReglaDeNegocioException("Ya existe una categoría con el nombre 'Bebidas'.");

        Assert.Equal("Ya existe una categoría con el nombre 'Bebidas'.", ex.Message);
    }
}
