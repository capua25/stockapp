using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class IconoAdjuntoConverterTests
{
    [Theory]
    [InlineData("application/pdf", "mdi-file-pdf-box")]
    [InlineData("image/jpeg", "mdi-file-image-outline")]
    [InlineData("image/png", "mdi-file-image-outline")]
    [InlineData("application/octet-stream", "mdi-file-outline")]
    [InlineData(null, "mdi-file-outline")]
    public void IconoPorTipo(string? contentType, string esperado) =>
        Assert.Equal(esperado, IconoAdjuntoConverter.Instance.Convert(contentType, typeof(string), null, CultureInfo.InvariantCulture));
}
