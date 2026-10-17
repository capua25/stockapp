using System;
using System.Globalization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class EstadoDocumentoConverterTests
{
    [Theory]
    [InlineData(EstadoDocumento.Pendiente, "Pendiente", VarianteInsignia.Neutro)]
    [InlineData(EstadoDocumento.EnProceso, "En proceso", VarianteInsignia.Advertencia)]
    [InlineData(EstadoDocumento.Finalizado, "Finalizado", VarianteInsignia.Exito)]
    [InlineData(EstadoDocumento.Anulado, "Anulado", VarianteInsignia.Neutro)]
    public void CadaEstado_TieneSuTextoYSuTono(EstadoDocumento estado, string texto, VarianteInsignia variante)
    {
        Assert.Equal(texto, EstadoDocumentoConverter.Texto.Convert(estado, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(texto, EstadoDocumentoConverter.Texto.Convert(estado.ToString(), typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(variante, EstadoDocumentoConverter.Variante.Convert(estado, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
        Assert.Equal(variante, EstadoDocumentoConverter.Variante.Convert(estado.ToString(), typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(EstadoDocumento.Pendiente, false)]
    [InlineData(EstadoDocumento.EnProceso, false)]
    [InlineData(EstadoDocumento.Finalizado, false)]
    [InlineData(EstadoDocumento.Anulado, true)]
    public void Inactivo_SoloElAnulado(EstadoDocumento estado, bool esperado)
        => Assert.Equal(esperado, EstadoDocumentoConverter.Inactivo.Convert(estado, typeof(bool), null, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(null)]
    [InlineData("Otro")]
    public void ValorDesconocido_TextoVacioNeutroYActivo(object? valor)
    {
        Assert.Equal(string.Empty, EstadoDocumentoConverter.Texto.Convert(valor, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(VarianteInsignia.Neutro, EstadoDocumentoConverter.Variante.Convert(valor, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, EstadoDocumentoConverter.Inactivo.Convert(valor, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_Lanza()
        => Assert.Throws<NotSupportedException>(() => EstadoDocumentoConverter.Texto.ConvertBack("x", typeof(EstadoDocumento), null, CultureInfo.InvariantCulture));
}
