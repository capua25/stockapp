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

    [Fact]
    public void UnTextoDesconocido_ComoElValorEsperado_EsNeutro()
    {
        var variante = VarianteEstadoGastoConverter.Instance.Convert("Otro", typeof(VarianteInsignia), null, CultureInfo.InvariantCulture);

        Assert.Equal(VarianteInsignia.Neutro, variante);
    }

    /// <summary>Caracterización: el converter compara el TEXTO del estado (lo que expone GastoFila), no el enum. Un
    /// EstadoGasto crudo cae en Neutro aunque sea Vencida.</summary>
    [Fact]
    public void UnValorQueNoEsTexto_AunqueSeaElEnum_EsNeutro()
    {
        var delEnum = VarianteEstadoGastoConverter.Instance.Convert(EstadoGasto.Vencida, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture);
        var numero = VarianteEstadoGastoConverter.Instance.Convert(3, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture);

        Assert.Equal(VarianteInsignia.Neutro, delEnum);
        Assert.Equal(VarianteInsignia.Neutro, numero);
    }

    [Fact]
    public void ConvertBack_NoEsSoportado()
    {
        Assert.Throws<NotSupportedException>(() =>
            VarianteEstadoGastoConverter.Instance.ConvertBack(VarianteInsignia.Peligro, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
