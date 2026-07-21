using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class ValidacionImportacionExceptionTests
{
    [Fact]
    public void Constructor_ExponeElDiccionarioDeErroresRecibido()
    {
        var errores = new Dictionary<string, string[]>
        {
            ["Gastos[0].Detalle"] = new[] { "Requerido" },
        };

        var ex = new ValidacionImportacionException(errores);

        Assert.Same(errores, ex.Errores);
        Assert.Equal("Requerido", ex.Errores["Gastos[0].Detalle"][0]);
    }
}
