using Microsoft.Extensions.Configuration;
using StockApp.Api.Auth;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class JwtOptionsFactoryTests
{
    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    [Fact]
    public void Crear_SinExpiracionHoras_UsaDefault12Horas()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "clave-de-prueba-de-al-menos-32-caracteres-x",
        });

        var options = JwtOptionsFactory.Crear(config);

        Assert.Equal(TimeSpan.FromHours(12), options.Expiracion);
    }

    [Fact]
    public void Crear_ConExpiracionHorasConfigurada_UsaElValorProvisto()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "clave-de-prueba-de-al-menos-32-caracteres-x",
            ["Jwt:ExpiracionHoras"] = "24",
        });

        var options = JwtOptionsFactory.Crear(config);

        Assert.Equal(TimeSpan.FromHours(24), options.Expiracion);
    }

    [Fact]
    public void Crear_SinSecret_LanzaInvalidOperationException()
    {
        var config = Config(new Dictionary<string, string?>());

        Assert.Throws<InvalidOperationException>(() => JwtOptionsFactory.Crear(config));
    }
}
