using System.Globalization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class MotivoMovimientoConverterTests
{
    [Theory]
    [InlineData(MotivoMovimiento.UsoOConsumo, "Uso o consumo")]
    [InlineData(MotivoMovimiento.Merma, "Merma")]
    public void Convert_DevuelveElTextoVisible(MotivoMovimiento motivo, string esperado)
        => Assert.Equal(esperado,
            MotivoMovimientoConverter.Instance.Convert(motivo, typeof(string), null, CultureInfo.InvariantCulture));

    [Fact]
    public void Convert_ValorNoMotivo_DevuelveVacio()
        => Assert.Equal(string.Empty,
            MotivoMovimientoConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
