using System;
using StockApp.Presentation.Helpers;
using Xunit;

namespace StockApp.Presentation.Tests.Helpers;

public class NormalizadorFechaHelperTests
{
    [Theory]
    [InlineData("25091999", 1999, 9, 25)]
    [InlineData("01012026", 2026, 1, 1)]
    [InlineData("29022024", 2024, 2, 29)] // año bisiesto
    public void TryNormalizarFecha_OchoDigitosValidos_DevuelveFechaCorrecta(
        string entrada, int anio, int mes, int dia)
    {
        var resultado = NormalizadorFechaHelper.TryNormalizarFecha(entrada, out var fecha);

        Assert.True(resultado);
        Assert.Equal(new DateTime(anio, mes, dia), fecha);
    }

    [Theory]
    [InlineData("99999999")]      // fecha inexistente (mes/día inválidos)
    [InlineData("29022023")]      // 2023 no es bisiesto
    [InlineData("25/09/1999")]    // ya tiene separadores, no es el caso de 8 dígitos
    [InlineData("")]              // vacío
    [InlineData("abcdefgh")]      // no numérico
    [InlineData("2509199")]       // 7 dígitos
    [InlineData("250919990")]     // 9 dígitos
    [InlineData(null)]            // nulo
    public void TryNormalizarFecha_EntradaInvalida_DevuelveFalse(string? entrada)
    {
        var resultado = NormalizadorFechaHelper.TryNormalizarFecha(entrada, out var fecha);

        Assert.False(resultado);
        Assert.Equal(default, fecha);
    }
}
