using System;
using System.Globalization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class EstadoTareaConverterTests
{
    [Theory]
    [InlineData(EstadoTarea.Pendiente, "Pendiente", VarianteInsignia.Neutro)]
    [InlineData(EstadoTarea.EnCurso, "En curso", VarianteInsignia.Advertencia)]
    [InlineData(EstadoTarea.Terminada, "Terminada", VarianteInsignia.Exito)]
    [InlineData(EstadoTarea.Cancelada, "Cancelada", VarianteInsignia.Neutro)]
    public void CadaEstado_TieneSuTextoYSuTono(EstadoTarea estado, string texto, VarianteInsignia variante)
    {
        Assert.Equal(texto, EstadoTareaConverter.Texto.Convert(estado, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(texto, EstadoTareaConverter.Texto.Convert(estado.ToString(), typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(variante, EstadoTareaConverter.Variante.Convert(estado, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
        Assert.Equal(variante, EstadoTareaConverter.Variante.Convert(estado.ToString(), typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(EstadoTarea.Pendiente, false)]
    [InlineData(EstadoTarea.EnCurso, false)]
    [InlineData(EstadoTarea.Terminada, false)]
    [InlineData(EstadoTarea.Cancelada, true)]
    public void Inactivo_SoloLaCancelada(EstadoTarea estado, bool esperado)
        => Assert.Equal(esperado, EstadoTareaConverter.Inactivo.Convert(estado, typeof(bool), null, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(null)]
    [InlineData("Otro")]
    public void ValorDesconocido_TextoVacioNeutroYActivo(object? valor)
    {
        Assert.Equal(string.Empty, EstadoTareaConverter.Texto.Convert(valor, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(VarianteInsignia.Neutro, EstadoTareaConverter.Variante.Convert(valor, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, EstadoTareaConverter.Inactivo.Convert(valor, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_Lanza()
        => Assert.Throws<NotSupportedException>(() => EstadoTareaConverter.Texto.ConvertBack("x", typeof(EstadoTarea), null, CultureInfo.InvariantCulture));
}
