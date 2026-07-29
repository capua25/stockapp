using StockApp.Api.Logging;

namespace StockApp.Api.Tests.Logging;

public class SaneadorCredencialesTests
{
    [Fact]
    public void Sanear_ConPasswordEnConnectionString_LaEnmascara()
    {
        const string texto = "Host=localhost;Database=stockapp;Username=postgres;Password=supersecreta123;";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("supersecreta123", resultado);
        Assert.Contains("Password=***", resultado);
        Assert.Contains("Host=localhost", resultado);
    }

    [Fact]
    public void Sanear_ConSecret_LoEnmascara()
    {
        const string texto = "Jwt:Secret=clave-de-firma-de-32-caracteres-abcdef y sigue el mensaje";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("clave-de-firma-de-32-caracteres-abcdef", resultado);
        Assert.Contains("Secret=***", resultado);
        Assert.Contains("y sigue el mensaje", resultado);
    }

    [Fact]
    public void Sanear_ConTokenBearer_LoEnmascara()
    {
        const string texto = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc.def";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc.def", resultado);
        Assert.Contains("Bearer ***", resultado);
    }

    [Fact]
    public void Sanear_EsInsensibleAMayusculas()
    {
        const string texto = "PASSWORD=otra-secreta;secret=tambien-secreta;BEARER token-secreto";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("otra-secreta", resultado);
        Assert.DoesNotContain("tambien-secreta", resultado);
        Assert.DoesNotContain("token-secreto", resultado);
    }

    [Fact]
    public void Sanear_ConVariasCredencialesEnUnaLinea_LasEnmascaraTodas()
    {
        const string texto = "Password=uno;Secret=dos;Bearer tres";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("uno", resultado);
        Assert.DoesNotContain("dos", resultado);
        Assert.DoesNotContain("tres", resultado);
    }

    [Fact]
    public void Sanear_SinCredenciales_DevuelveElTextoIntacto()
    {
        const string texto = "Fallo la corrida de backup: el binario pg_dump no existe en la ruta configurada.";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.Equal(texto, resultado);
    }

    [Fact]
    public void Sanear_TextoVacio_NoRompe()
    {
        Assert.Equal(string.Empty, SaneadorCredenciales.Sanear(string.Empty));
    }

    [Fact]
    public void Sanear_ConPasswordEntreComillasDobles_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Password=\"p@ss;word\"";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("p@ss;word", resultado);
        Assert.Contains("Password=***", resultado);
    }

    [Fact]
    public void Sanear_ConPasswordEntreComillasSimples_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Password='p@ss;word'";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("p@ss;word", resultado);
        Assert.Contains("Password=***", resultado);
    }

    [Fact]
    public void Sanear_ConSecretEntreComillasDobles_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Secret=\"cl;ave-secreta\"";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("cl;ave-secreta", resultado);
        Assert.Contains("Secret=***", resultado);
    }

    [Fact]
    public void Sanear_ConSecretEntreComillasSimples_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Secret='cl;ave-secreta'";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("cl;ave-secreta", resultado);
        Assert.Contains("Secret=***", resultado);
    }

    [Fact]
    public void Sanear_ConBearerEntreComillasDobles_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Bearer \"tok;en-secreto\"";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("tok;en-secreto", resultado);
        Assert.Contains("Bearer ***", resultado);
    }

    [Fact]
    public void Sanear_ConBearerEntreComillasSimples_ConPuntoYComaAdentro_LoEnmascara()
    {
        const string texto = "Bearer 'tok;en-secreto'";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("tok;en-secreto", resultado);
        Assert.Contains("Bearer ***", resultado);
    }

    // ── Bypasses encontrados en el review final (Entrega 2) ────────────────────────

    [Fact]
    public void Sanear_ConPasswordSinComillasConEspacios_NoDejaElRestoDeLaCredencialEnClaro()
    {
        const string texto = "Password=hola mundo;Host=x";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("mundo", resultado);
        Assert.Contains("Host=x", resultado);
    }

    [Fact]
    public void Sanear_ConPasswordEntreComillasSinCerrar_NoDejaLaCredencialEnClaro()
    {
        const string texto = "Password=\"abc sin cerrar";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("abc sin cerrar", resultado);
    }

    [Fact]
    public void Sanear_ConAliasPwd_LoEnmascara()
    {
        const string texto = "Pwd=secreto123;Host=x";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("secreto123", resultado);
        Assert.Contains("Host=x", resultado);
    }

    [Fact]
    public void Sanear_ConAliasPsw_LoEnmascara()
    {
        const string texto = "Psw=otra;Host=x";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("otra", resultado);
        Assert.Contains("Host=x", resultado);
    }

    [Fact]
    public void Sanear_ConPassword_NoSeComeElRestoDeLaConnectionString()
    {
        const string texto = "Password=abc;Host=localhost;Database=stockapp";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.Contains("Host=localhost", resultado);
        Assert.Contains("Database=stockapp", resultado);
    }

    [Fact]
    public void Sanear_ConSecretSinComillasConEspacios_NoDejaElRestoEnClaro()
    {
        const string texto = "Secret=con espacios;otra=cosa";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("espacios", resultado);
        Assert.Contains("otra=cosa", resultado);
    }

    [Fact]
    public void Sanear_ConBearerYTextoDespuesEnLaMismaLinea_NoSeComeElTextoPosterior()
    {
        const string texto = "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc.def resto del mensaje";

        var resultado = SaneadorCredenciales.Sanear(texto);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9.abc.def", resultado);
        Assert.Contains("resto del mensaje", resultado);
    }
}
