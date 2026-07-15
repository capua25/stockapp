using System.Security.Cryptography;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Licenciamiento;

public class ServicioResetAdminTests
{
    private const string Maquina = "MAQ-RESET-1";

    // ── Fakes ────────────────────────────────────────────────────────────────
    private sealed class FingerprintFake : IFingerprintMaquina
    {
        public string CodigoAgrupado => Maquina;
    }

    private sealed class HasherFake : IPasswordHasher
    {
        public string Hash(string plana) => "HASH:" + plana;
        public bool Verify(string plana, string hash) => hash == "HASH:" + plana;
    }

    private sealed class AuditFake : IAuditLogger
    {
        public List<(int usuarioId, AccionAuditada accion)> Eventos { get; } = new();
        public Task RegistrarAsync(int usuarioId, AccionAuditada accion, string entidad, int entidadId, string detalle)
        {
            Eventos.Add((usuarioId, accion));
            return Task.CompletedTask;
        }
    }

    private sealed class UsuarioRepoFake : IUsuarioRepository
    {
        public List<Usuario> Usuarios { get; } = new();
        private int _seq = 1;

        public Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario)
            => Task.FromResult(Usuarios.FirstOrDefault(u => u.NombreUsuario == nombreUsuario));
        public Task<Usuario?> ObtenerPorIdAsync(int id)
            => Task.FromResult(Usuarios.FirstOrDefault(u => u.Id == id));
        public Task<IReadOnlyList<Usuario>> ListarTodosAsync()
            => Task.FromResult((IReadOnlyList<Usuario>)Usuarios.ToList());
        public Task<bool> ExisteAlgunUsuarioAsync() => Task.FromResult(Usuarios.Count > 0);
        public Task<int> ContarAdminsActivosAsync()
            => Task.FromResult(Usuarios.Count(u => u.Rol == RolUsuario.Admin && u.Activo));
        public Task<int> AgregarAsync(Usuario usuario)
        {
            usuario.Id = _seq++;
            Usuarios.Add(usuario);
            return Task.FromResult(usuario.Id);
        }
        public Task ActualizarAsync(Usuario usuario) => Task.CompletedTask;
        public Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso) => Task.CompletedTask;
    }

    private sealed class Contexto
    {
        public required ServicioResetAdmin Servicio { get; init; }
        public required IAlmacenDesafiosReset Desafios { get; init; }
        public required UsuarioRepoFake Repo { get; init; }
        public required AuditFake Audit { get; init; }
        public required string PrivadaPem { get; init; }
    }

    private static Contexto Armar(bool conAdmin)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var validador = new ValidadorFirma(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        var privada = ecdsa.ExportPkcs8PrivateKeyPem();

        var repo = new UsuarioRepoFake();
        var hasher = new HasherFake();
        if (conAdmin)
            repo.Usuarios.Add(new Usuario
            {
                Id = 7, NombreUsuario = "admin", HashContrasena = "HASH:vieja",
                Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow,
            });

        var desafios = new AlmacenDesafiosResetEnMemoria();
        var audit = new AuditFake();
        var primerArranque = new PrimerArranqueService(repo, hasher);
        var servicio = new ServicioResetAdmin(
            validador, new FingerprintFake(), desafios, repo, hasher, primerArranque, audit);

        return new Contexto
        {
            Servicio = servicio, Desafios = desafios, Repo = repo, Audit = audit, PrivadaPem = privada,
        };
    }

    private static string Token(string privada, string desafio, string maquina = Maquina, string accion = "reset-admin")
        => FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, accion, maquina, desafio), privada);

    [Fact]
    public async Task Resetear_TokenValido_CambiaLaContrasenaDelAdminYAudita()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(Token(c.PrivadaPem, desafio), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.Valido, resultado);
        Assert.Equal("HASH:nueva-clave-123", c.Repo.Usuarios.Single().HashContrasena);
        Assert.Contains(c.Audit.Eventos, e => e.accion == AccionAuditada.ResetAdminFirmado && e.usuarioId == 7);
    }

    [Fact]
    public async Task Resetear_AdminInactivo_LoReactiva()
    {
        var c = Armar(conAdmin: true);
        c.Repo.Usuarios.Single().Activo = false;
        var desafio = c.Desafios.GenerarNuevo();

        await c.Servicio.ResetearAsync(Token(c.PrivadaPem, desafio), "nueva-clave-123");

        Assert.True(c.Repo.Usuarios.Single().Activo);
    }

    [Fact]
    public async Task Resetear_SinAdmin_RecreaAdminViaPrimerArranque()
    {
        var c = Armar(conAdmin: false);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(Token(c.PrivadaPem, desafio), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.Valido, resultado);
        var admin = c.Repo.Usuarios.Single(u => u.Rol == RolUsuario.Admin);
        Assert.Equal("HASH:nueva-clave-123", admin.HashContrasena);
    }

    [Fact]
    public async Task Resetear_TokenReusado_LaSegundaVezDaDesafioInvalido()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();
        var token = Token(c.PrivadaPem, desafio);

        await c.Servicio.ResetearAsync(token, "nueva-clave-123");
        var segunda = await c.Servicio.ResetearAsync(token, "otra-clave-456");

        Assert.Equal(ResultadoValidacionReset.DesafioInvalido, segunda);
    }

    [Fact]
    public async Task Resetear_MaquinaDistinta_DevuelveMaquinaDistinta()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(
            Token(c.PrivadaPem, desafio, maquina: "OTRA"), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.MaquinaDistinta, resultado);
    }

    [Fact]
    public async Task Resetear_AccionEquivocada_DevuelveAccionInvalida()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();

        var resultado = await c.Servicio.ResetearAsync(
            Token(c.PrivadaPem, desafio, accion: "otra-cosa"), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.AccionInvalida, resultado);
    }

    [Fact]
    public async Task Resetear_DesafioDesconocido_DevuelveDesafioInvalido()
    {
        var c = Armar(conAdmin: true);
        c.Desafios.GenerarNuevo(); // hay uno vivo, pero el token trae otro

        var resultado = await c.Servicio.ResetearAsync(
            Token(c.PrivadaPem, "nonce-que-no-existe"), "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.DesafioInvalido, resultado);
    }

    [Fact]
    public async Task Resetear_FirmaInvalida_DevuelveFirmaInvalido()
    {
        var c = Armar(conAdmin: true);
        var desafio = c.Desafios.GenerarNuevo();
        using var otra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var tokenMalFirmado = Token(otra.ExportPkcs8PrivateKeyPem(), desafio);

        var resultado = await c.Servicio.ResetearAsync(tokenMalFirmado, "nueva-clave-123");

        Assert.Equal(ResultadoValidacionReset.FirmaInvalido, resultado);
    }
}
