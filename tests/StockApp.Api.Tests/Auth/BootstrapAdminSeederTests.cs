using System.Threading.Tasks;
using StockApp.Api.Auth;
using StockApp.Application.Auth;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class BootstrapAdminSeederTests
{
    // Fake manual de IPrimerArranqueService: evita depender de un paquete de mocking
    // en este proyecto de tests. Registra las llamadas para poder asertar.
    private sealed class PrimerArranqueFake : IPrimerArranqueService
    {
        private readonly bool _requiere;
        public PrimerArranqueFake(bool requiere) => _requiere = requiere;

        public int VecesCreado { get; private set; }
        public string? UltimoUsuario { get; private set; }
        public string? UltimaContrasena { get; private set; }

        public Task<bool> RequiereCrearAdminAsync() => Task.FromResult(_requiere);

        public Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
        {
            VecesCreado++;
            UltimoUsuario = nombreUsuario;
            UltimaContrasena = contrasenaPlana;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SembrarAsync_CuandoYaHayUsuarios_NoCreaAdmin()
    {
        var fake = new PrimerArranqueFake(requiere: false);
        var seeder = new BootstrapAdminSeeder(fake, "admin", "secreta123");

        await seeder.SembrarAsync();

        Assert.Equal(0, fake.VecesCreado);
    }

    [Fact]
    public async Task SembrarAsync_DbVirgenConCredenciales_CreaAdmin()
    {
        var fake = new PrimerArranqueFake(requiere: true);
        var seeder = new BootstrapAdminSeeder(fake, "admin", "secreta123");

        await seeder.SembrarAsync();

        Assert.Equal(1, fake.VecesCreado);
        Assert.Equal("admin", fake.UltimoUsuario);
        Assert.Equal("secreta123", fake.UltimaContrasena);
    }

    [Theory]
    [InlineData(null, "secreta123")]
    [InlineData("", "secreta123")]
    [InlineData("   ", "secreta123")]
    [InlineData("admin", null)]
    [InlineData("admin", "")]
    [InlineData("admin", "   ")]
    public async Task SembrarAsync_DbVirgenSinCredenciales_LanzaInvalidOperation(string? user, string? pass)
    {
        var fake = new PrimerArranqueFake(requiere: true);
        var seeder = new BootstrapAdminSeeder(fake, user, pass);

        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SembrarAsync());
        Assert.Equal(0, fake.VecesCreado);
    }
}
