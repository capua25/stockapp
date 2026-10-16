using System.Globalization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class VarianteEstadoGastoConverterTests
{
    [Theory]
    [InlineData(nameof(EstadoGasto.Vencida), VarianteInsignia.Peligro)]
    [InlineData(nameof(EstadoGasto.Parcial), VarianteInsignia.Advertencia)]
    [InlineData(nameof(EstadoGasto.Pagada), VarianteInsignia.Exito)]
    [InlineData(nameof(EstadoGasto.Pendiente), VarianteInsignia.Neutro)]
    [InlineData(nameof(EstadoGasto.Anulada), VarianteInsignia.Neutro)]
    [InlineData(null, VarianteInsignia.Neutro)]
    public void CadaEstado_TieneSuTono(string? estado, VarianteInsignia esperada)
    {
        var variante = VarianteEstadoGastoConverter.Instance.Convert(estado, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture);

        Assert.Equal(esperada, variante);
    }
}
