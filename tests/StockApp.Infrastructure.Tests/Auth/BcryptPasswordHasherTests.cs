using StockApp.Infrastructure.Auth;
using Xunit;

namespace StockApp.Infrastructure.Tests.Auth;

public class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProduceHashNoVacio()
    {
        var hash = _hasher.Hash("miContrasena123");

        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void Verify_ConContrasenaCorrecta_RetornaTrue()
    {
        var hash = _hasher.Hash("contrasenaCorrecta");

        Assert.True(_hasher.Verify("contrasenaCorrecta", hash));
    }

    [Fact]
    public void Verify_ConContrasenaIncorrecta_RetornaFalse()
    {
        var hash = _hasher.Hash("contrasenaCorrecta");

        Assert.False(_hasher.Verify("contrasenaIncorrecta", hash));
    }

    [Fact]
    public void Hash_MismaContrasena_ProduceDosHashesDistintos()
    {
        // BCrypt embebe sal aleatoria en cada hash → misma entrada, hashes distintos
        var hash1 = _hasher.Hash("mismaContrasena");
        var hash2 = _hasher.Hash("mismaContrasena");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_NoGuardaContrasenaEnTextoPlano()
    {
        var contrasena = "secreto123";
        var hash = _hasher.Hash(contrasena);

        Assert.DoesNotContain(contrasena, hash);
    }
}
