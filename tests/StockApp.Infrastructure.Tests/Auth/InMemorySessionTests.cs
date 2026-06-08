using StockApp.Application.Auth;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using Xunit;

namespace StockApp.Infrastructure.Tests.Auth;

public class InMemorySessionTests
{
    private readonly InMemorySession _session = new();

    private static Usuario UsuarioAdmin() => new()
    {
        Id = 1,
        NombreUsuario = "admin",
        HashContrasena = "hash",
        Rol = RolUsuario.Admin,
        Activo = true,
        FechaAlta = DateTime.UtcNow
    };

    [Fact]
    public void SesionNueva_NoEstaAutenticada()
    {
        Assert.False(_session.EstaAutenticado);
        Assert.Null(_session.UsuarioActual);
    }

    [Fact]
    public void Login_EstableceSesion_Y_EstaAutenticadoEsTrue()
    {
        _session.IniciarSesion(UsuarioAdmin());

        Assert.True(_session.EstaAutenticado);
        Assert.NotNull(_session.UsuarioActual);
        Assert.Equal("admin", _session.UsuarioActual!.NombreUsuario);
    }

    [Fact]
    public void Login_SnapshotNoContieneHash()
    {
        // La sesión guarda un snapshot inmutable sin HashContrasena
        _session.IniciarSesion(UsuarioAdmin());

        var snapshot = _session.UsuarioActual!;
        Assert.IsType<UsuarioSesion>(snapshot);
        // UsuarioSesion no tiene propiedad HashContrasena — no compila si la agregás
        Assert.Equal(1, snapshot.Id);
        Assert.Equal("admin", snapshot.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, snapshot.Rol);
    }

    [Fact]
    public void Logout_LimpiaSesion()
    {
        _session.IniciarSesion(UsuarioAdmin());
        _session.CerrarSesion();

        Assert.False(_session.EstaAutenticado);
        Assert.Null(_session.UsuarioActual);
    }

    [Fact]
    public void CambioDeUsuario_SinCerrarApp_EsposibleHaciendoNuevoLogin()
    {
        var operador = new Usuario
        {
            Id = 2,
            NombreUsuario = "operador1",
            HashContrasena = "hash",
            Rol = RolUsuario.Operador,
            Activo = true,
            FechaAlta = DateTime.UtcNow
        };

        _session.IniciarSesion(UsuarioAdmin());
        _session.CerrarSesion();
        _session.IniciarSesion(operador);

        Assert.Equal("operador1", _session.UsuarioActual!.NombreUsuario);
        Assert.Equal(RolUsuario.Operador, _session.UsuarioActual.Rol);
    }

    [Fact]
    public void RolActual_SinSesion_EsNull()
    {
        Assert.Null(_session.RolActual);
    }

    [Fact]
    public void RolActual_ConSesion_RetornaRolDelUsuario()
    {
        _session.IniciarSesion(UsuarioAdmin());

        Assert.Equal(RolUsuario.Admin, _session.RolActual);
    }
}
