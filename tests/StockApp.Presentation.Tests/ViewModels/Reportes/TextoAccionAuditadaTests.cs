using System;
using System.Linq;
using System.Text.RegularExpressions;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class TextoAccionAuditadaTests
{
    public static TheoryData<AccionAuditada> TodosLosValores()
    {
        var datos = new TheoryData<AccionAuditada>();
        foreach (var valor in Enum.GetValues<AccionAuditada>())
            datos.Add(valor);
        return datos;
    }

    [Theory]
    [MemberData(nameof(TodosLosValores))]
    public void CadaValorDelEnum_TieneTextoLegible_NoElNombreCrudo(AccionAuditada accion)
    {
        var texto = TextoAccionAuditada.De(accion);

        Assert.False(string.IsNullOrWhiteSpace(texto));
        Assert.NotEqual(accion.ToString(), texto);
        // Nombre crudo = PascalCase pegado ("RegistroMovimiento"): un texto legible lleva espacios entre palabras.
        Assert.DoesNotMatch(new Regex("[a-záéíóú][A-Z]"), texto);
        Assert.Contains(' ', texto);
    }

    [Fact]
    public void LosTextosSonUnicos_NoHayDosAccionesConElMismoTexto()
    {
        var textos = Enum.GetValues<AccionAuditada>().Select(TextoAccionAuditada.De).ToList();

        Assert.Equal(textos.Count, textos.Distinct().Count());
    }

    [Theory]
    [InlineData(AccionAuditada.RegistroMovimiento, "Registro de movimiento")]
    [InlineData(AccionAuditada.ModificacionProducto, "Modificación de producto")]
    [InlineData(AccionAuditada.CambioPrecio, "Cambio de precio")]
    [InlineData(AccionAuditada.CambioContrasena, "Cambio de contraseña")]
    [InlineData(AccionAuditada.RecalculoStock, "Recálculo de stock")]
    public void TextosAprobados(AccionAuditada accion, string esperado) =>
        Assert.Equal(esperado, TextoAccionAuditada.De(accion));

    [Fact]
    public void UnValorDesconocido_SeMuestraConSuNombre_SinRomper()
    {
        var desconocido = (AccionAuditada)9999;

        Assert.Equal("9999", TextoAccionAuditada.De(desconocido));
    }

    [Fact]
    public void Converter_UsaElMismoTexto_YUnValorQueNoEsAccionPasaTal()
    {
        var c = AccionAuditadaConverter.Instance;

        Assert.Equal("Registro de movimiento", c.Convert(AccionAuditada.RegistroMovimiento, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("9999", c.Convert((AccionAuditada)9999, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("otra cosa", c.Convert("otra cosa", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(c.Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
