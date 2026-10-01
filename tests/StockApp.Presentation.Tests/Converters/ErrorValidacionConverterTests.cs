using System;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Política del proyecto: ninguna excepción cruda de .NET debe mostrarse en la UI como mensaje
/// de validación. <see cref="ErrorValidacionConverter"/> se asigna globalmente a
/// <c>DataValidationErrors.ErrorConverter</c> (ver Themes/Controls.axaml) y transforma cada
/// error antes de que quede expuesto en <c>DataValidationErrors.Errors</c>.
/// </summary>
public class ErrorValidacionConverterTests
{
    [Fact]
    public void Excepcion_SeReemplazaPorMensajeDeDominioGenerico()
    {
        var resultado = ErrorValidacionConverter.Instance(new InvalidCastException("Could not convert \"\" to decimal."));

        Assert.Equal("Ingresá un número válido.", resultado);
        Assert.DoesNotContain("Exception", (string)resultado);
        Assert.DoesNotContain("System.", (string)resultado);
    }

    [Fact]
    public void CualquierTipoDeExcepcion_SeReemplazaPorElMismoMensaje()
    {
        var resultado = ErrorValidacionConverter.Instance(new FormatException("Input string was not in a correct format."));

        Assert.Equal("Ingresá un número válido.", resultado);
    }

    [Fact]
    public void MensajeDeDominio_StringPlano_PasaSinModificar()
    {
        const string mensajeDominio = "El campo es obligatorio.";

        var resultado = ErrorValidacionConverter.Instance(mensajeDominio);

        Assert.Same(mensajeDominio, resultado);
    }

    /// <summary>El rechazo de un número mal escrito (ej. "5.4" → "Usá coma para los decimales: 5,4")
    /// es un mensaje de DOMINIO armado por FormatoEsUy, no una excepción cruda de .NET: tiene que
    /// llegar tal cual al operador, si no el genérico "Ingresá un número válido." le esconde el porqué.</summary>
    [Fact]
    public void EntradaNumericaInvalida_PasaConSuMensajeDeDominio()
    {
        var resultado = ErrorValidacionConverter.Instance(
            new EntradaNumericaInvalidaException("Usá coma para los decimales: 5,4"));

        Assert.Equal("Usá coma para los decimales: 5,4", resultado);
    }
}
