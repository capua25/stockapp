using StockApp.Application.Auth;
using Xunit;

namespace StockApp.Application.Tests.Auth;

// Regla vigente: mínimo 8 caracteres, con al menos una letra y al menos un número.
public class ContrasenaValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validar_NuloVacioOWhitespace_LanzaArgumentException(string? contrasena)
    {
        var ex = Assert.Throws<ArgumentException>(() => ContrasenaValidator.Validar(contrasena));

        Assert.Equal("La contraseña no puede estar vacía.", ex.Message);
    }

    [Theory]
    [InlineData("abc123z")]      // 7 chars, letra y número — borde inferior, un char corto
    [InlineData("abcdefgh")]     // 8+ chars, solo letras
    [InlineData("12345678")]     // 8+ chars, solo números
    [InlineData("abcdefghij")]   // largo, solo letras
    public void Validar_IncumpleLaRegla_LanzaArgumentException(string contrasena)
    {
        var ex = Assert.Throws<ArgumentException>(() => ContrasenaValidator.Validar(contrasena));

        Assert.Equal(
            "La contraseña debe tener al menos 8 caracteres e incluir al menos una letra y un número.",
            ex.Message);
    }

    [Fact]
    public void Validar_ExactamenteOchoCharsConLetraYNumero_Pasa()
    {
        // Borde exacto: 8 caracteres, cumple letra + número.
        var exception = Record.Exception(() => ContrasenaValidator.Validar("abcd1234"));

        Assert.Null(exception);
    }

    [Fact]
    public void Validar_ConLetraAcentuadaYNumero_Pasa()
    {
        // Guarda el criterio IsLetter unicode: acentos y ñ son letras válidas
        // (municipio argentino, no restringimos a ASCII).
        var exception = Record.Exception(() => ContrasenaValidator.Validar("contraseña1"));

        Assert.Null(exception);
    }

    [Fact]
    public void Validar_ContrasenaLargaYValida_Pasa()
    {
        var exception = Record.Exception(() => ContrasenaValidator.Validar("estaEsUnaContrasenaBienLarga2026"));

        Assert.Null(exception);
    }
}
