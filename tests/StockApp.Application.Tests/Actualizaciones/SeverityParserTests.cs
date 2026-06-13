using StockApp.Application.Actualizaciones;

namespace StockApp.Application.Tests.Actualizaciones;

public class SeverityParserTests
{
    private readonly SeverityParser _sut = new();

    [Fact]
    public void Parse_Critical_ReturnsCritical()
    {
        var resultado = _sut.Parse("severity: critical\n\n## Qué cambió\n- Fix urgente");

        Assert.Equal(UpdateSeverity.Critical, resultado);
    }

    [Fact]
    public void Parse_Important_ReturnsImportant()
    {
        var resultado = _sut.Parse("severity: important\n\nAlgunos cambios importantes");

        Assert.Equal(UpdateSeverity.Important, resultado);
    }

    [Fact]
    public void Parse_Normal_ReturnsNormal()
    {
        var resultado = _sut.Parse("severity: normal\n\nCambios menores");

        Assert.Equal(UpdateSeverity.Normal, resultado);
    }

    [Fact]
    public void Parse_Ausente_DefaultNormal()
    {
        var resultado = _sut.Parse("## Qué cambió\n- Sin front-matter de severity");

        Assert.Equal(UpdateSeverity.Normal, resultado);
    }

    [Fact]
    public void Parse_Invalido_DefaultNormal()
    {
        var resultado = _sut.Parse("severity: urgente\n\nValor desconocido");

        Assert.Equal(UpdateSeverity.Normal, resultado);
    }

    [Fact]
    public void Parse_MayusculasYEspacios_Tolerado()
    {
        var resultado = _sut.Parse("Severity:  CRITICAL \n\nMayúsculas y espacios extra");

        Assert.Equal(UpdateSeverity.Critical, resultado);
    }

    [Fact]
    public void Parse_NotasNullOVacias_Normal()
    {
        Assert.Equal(UpdateSeverity.Normal, _sut.Parse(null));
        Assert.Equal(UpdateSeverity.Normal, _sut.Parse(""));
        Assert.Equal(UpdateSeverity.Normal, _sut.Parse("   "));
    }

    [Fact]
    public void Parse_SeverityNoEnPrimeraLinea_Normal()
    {
        var resultado = _sut.Parse("## Cambios\nseverity: critical\n\nLa severity no está en la primera línea");

        Assert.Equal(UpdateSeverity.Normal, resultado);
    }
}
