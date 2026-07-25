using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaRubroNuevoVmTests
{
    [Fact]
    public void Desde_MapeaCodigoYNombreSugerido()
    {
        var fila = FilaRubroNuevoVm.Desde(new CodigoRubroNuevoDto(42, "Materiales"));

        Assert.Equal(42, fila.Codigo);
        Assert.Equal("Materiales", fila.Nombre);
        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void Desde_SinNombreSugerido_TieneErrorDeValidacion()
    {
        var fila = FilaRubroNuevoVm.Desde(new CodigoRubroNuevoDto(42, null));

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Nombre)).Cast<object>());
    }

    [Fact]
    public void Nombre_SeCompleta_LimpiaElErrorDeValidacion()
    {
        var fila = FilaRubroNuevoVm.Desde(new CodigoRubroNuevoDto(42, null));

        fila.Nombre = "Materiales de obra";

        Assert.False(fila.HasErrors);
    }
}
